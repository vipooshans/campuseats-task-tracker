using LankaMart.Api.Common;
using LankaMart.Api.Dtos;
using LankaMart.Api.Models;
using LankaMart.Api.Repositories;
using LankaMart.Api.Security;
using Microsoft.Extensions.Options;

namespace LankaMart.Api.Services;

// ============================================================================
//  TWO LESSONS LIVE IN THIS FILE.
//
//  1. TRANSACTIONS (SLIDE 8, SLIDE 24 notes)
//     Placing an order writes to three tables: orders, order_items, and
//     products (stock). Either all of it happens or none of it does. If the
//     stock update succeeded and the order insert failed, LankaMart would have
//     sold goods that no order accounts for. PlaceOrderAsync owns that unit of
//     work.
//
//  2. OBJECT-LEVEL AUTHORIZATION (SLIDE 36, SLIDE 42 row 4)
//     [Authorize(Roles="Customer")] cannot express "only your OWN orders". A
//     role check is about the caller; this rule is about the caller AND the
//     row. Broken object-level authorization is number one in the OWASP API
//     Security Top 10, and it is a one-line bug: forget EnsureCanAccess and
//     customer A reads customer B's invoices by changing the id in the URL.
// ============================================================================
public sealed class OrderService : IOrderService
{
    private readonly IOrderRepository      _orders;
    private readonly IProductRepository    _products;
    private readonly IUnitOfWork           _uow;
    private readonly LankaMartOptions      _options;
    private readonly IClock                _clock;
    private readonly ILogger<OrderService> _logger;

    public OrderService(
        IOrderRepository orders,
        IProductRepository products,
        IUnitOfWork uow,
        IOptions<LankaMartOptions> options,
        IClock clock,
        ILogger<OrderService> logger)
    {
        _orders   = orders;
        _products = products;
        _uow      = uow;
        _options  = options.Value;
        _clock    = clock;
        _logger   = logger;
    }

    // ========================================================================
    //  PLACE AN ORDER
    // ========================================================================
    public async Task<OrderDto> PlaceOrderAsync(
        long customerId, CreateOrderDto dto, CancellationToken ct = default)
    {
        if (dto.Items.Count == 0)
            throw new BusinessRuleException("An order must contain at least one item.");

        // Collapse duplicate lines. order_items has a COMPOSITE PRIMARY KEY of
        // (order_id, product_id), so the same product twice in one order is a
        // primary-key violation, not a second row (SLIDE 20). Merging the
        // quantities is the sane interpretation of the request.
        var requested = dto.Items
            .GroupBy(i => i.ProductId)
            .Select(g => new { ProductId = g.Key, Quantity = g.Sum(i => i.Quantity) })
            .ToList();

        await using var tx = await _uow.BeginTransactionAsync(ct);

        // ONE query for the whole basket, tracked so stock changes are written.
        var products = await _products.GetForUpdateAsync(requested.Select(r => r.ProductId), ct);
        var byId     = products.ToDictionary(p => p.Id);

        var order = new Order
        {
            CustomerId    = customerId,       // from the JWT, never from the body
            PaymentMethod = dto.PaymentMethod.ToLowerInvariant(),
            CreatedAt     = _clock.UtcNow
        };

        foreach (var line in requested)
        {
            if (!byId.TryGetValue(line.ProductId, out var product))
                throw new NotFoundException("Product", line.ProductId);

            // 409, not 400: the request is well formed, the world disagrees
            // with it. Somebody else bought the last one.
            if (!product.CanFulfil(line.Quantity))
                throw new ConflictException(
                    $"Insufficient stock for '{product.Name}': requested {line.Quantity}, available {product.StockQty}.");

            product.StockQty -= line.Quantity;      // reserve it

            order.Items.Add(new OrderItem
            {
                ProductId = product.Id,
                Quantity  = line.Quantity,

                // SLIDE 15 - freeze the price. Tomorrow's price change must not
                // rewrite this invoice.
                UnitPrice = product.Price
            });
        }

        // Pricing rules, unchanged from Lecture 03 and still configuration
        // rather than magic numbers.
        var subtotal = order.Items.Sum(i => i.LineTotal);

        order.DeliveryFee = subtotal >= _options.FreeDeliveryThreshold ? 0m : _options.DeliveryFee;
        order.Surcharge   = order.PaymentMethod == "cash" ? _options.CashOnDeliverySurcharge : 0m;

        order.Confirm();

        await _orders.AddAsync(order, ct);

        // One SaveChangesAsync writes the order, its items and the stock
        // decrements; the transaction makes them one atomic unit. If the
        // products CHECK (stock_qty >= 0) fires because of a race, PostgreSQL
        // rejects the whole thing and no order survives.
        await _uow.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        _logger.LogInformation(
            "Customer {CustomerId} placed order {OrderId} for {Total}",
            customerId, order.Id, order.Total);

        var saved = await _orders.GetByIdWithItemsAsync(order.Id, ct);
        return ToDto(saved!);
    }

    // ========================================================================
    //  READ ONE ORDER - the object-level authorization demo
    // ========================================================================
    public async Task<OrderDto> GetByIdAsync(
        long orderId, long requestingUserId, bool callerIsStaffOrAdmin, CancellationToken ct = default)
    {
        var order = await _orders.GetByIdWithItemsAsync(orderId, ct)
                    ?? throw new NotFoundException("Order", orderId);

        EnsureCanAccess(order, requestingUserId, callerIsStaffOrAdmin);

        return ToDto(order);
    }

    public async Task<IReadOnlyList<OrderDto>> GetMyOrdersAsync(
        long customerId, CancellationToken ct = default)
    {
        // No authorization check needed here, and that is not laziness: the
        // filter IS the authorization. The query can only ever return rows
        // belonging to the caller, because customerId comes from their token.
        var orders = await _orders.GetByCustomerAsync(customerId, ct);
        return orders.Select(ToDto).ToList();
    }

    /// Staff / Admin only - enforced by [Authorize(Roles=...)] on the endpoint.
    public async Task<PagedResult<OrderDto>> SearchAsync(
        string? status, int page, int pageSize, CancellationToken ct = default)
    {
        page     = page < 1 ? 1 : page;
        pageSize = pageSize < 1
            ? _options.DefaultPageSize
            : Math.Min(pageSize, _options.MaxPageSize);

        var (items, total) = await _orders.SearchAsync(status, page, pageSize, ct);

        return new PagedResult<OrderDto>(items.Select(ToDto).ToList(), page, pageSize, total);
    }

    // ========================================================================
    //  CANCEL - ownership check plus a state machine
    // ========================================================================
    public async Task<OrderDto> CancelAsync(
        long orderId, long requestingUserId, bool callerIsStaffOrAdmin, CancellationToken ct = default)
    {
        await using var tx = await _uow.BeginTransactionAsync(ct);

        var order = await _orders.GetForUpdateAsync(orderId, ct)
                    ?? throw new NotFoundException("Order", orderId);

        // A customer may cancel their own order. A customer may NOT cancel
        // somebody else's, even though both callers hold the identical role -
        // which is precisely why the role check alone is not enough.
        EnsureCanAccess(order, requestingUserId, callerIsStaffOrAdmin);

        try
        {
            order.Cancel();                 // the entity guards illegal transitions
        }
        catch (InvalidOperationException ex)
        {
            throw new ConflictException(ex.Message);
        }

        // Put the stock back.
        var products = await _products.GetForUpdateAsync(order.Items.Select(i => i.ProductId), ct);
        var byId     = products.ToDictionary(p => p.Id);

        foreach (var item in order.Items)
            if (byId.TryGetValue(item.ProductId, out var product))
                product.StockQty += item.Quantity;

        await _uow.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        _logger.LogInformation("Order {OrderId} cancelled by user {UserId}", orderId, requestingUserId);

        var saved = await _orders.GetByIdWithItemsAsync(orderId, ct);
        return ToDto(saved!);
    }

    // ========================================================================
    //  REFUND - guarded by a PERMISSION, not a role   (SLIDE 38 bottom note)
    //  The endpoint requires the "orders.refund" permission claim, so which
    //  roles may refund can change without touching this code or the controller.
    // ========================================================================
    public async Task<OrderDto> RefundAsync(long orderId, CancellationToken ct = default)
    {
        await using var tx = await _uow.BeginTransactionAsync(ct);

        var order = await _orders.GetForUpdateAsync(orderId, ct)
                    ?? throw new NotFoundException("Order", orderId);

        if (order.Status == OrderStatus.Refunded)
            throw new ConflictException("This order has already been refunded.");
        if (order.Status == OrderStatus.Cancelled)
            throw new ConflictException("A cancelled order cannot be refunded.");

        order.Refund();

        var products = await _products.GetForUpdateAsync(order.Items.Select(i => i.ProductId), ct);
        var byId     = products.ToDictionary(p => p.Id);

        foreach (var item in order.Items)
            if (byId.TryGetValue(item.ProductId, out var product))
                product.StockQty += item.Quantity;

        await _uow.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        _logger.LogInformation("Order {OrderId} refunded", orderId);

        var saved = await _orders.GetByIdWithItemsAsync(orderId, ct);
        return ToDto(saved!);
    }

    // ------------------------------------------------------------------------
    //  THE FOUR LINES THAT OWASP API #1 IS ABOUT.
    //
    //  Design note worth debating in class: we answer 403 here, which follows
    //  slide 28 - the caller is authenticated, and refused. Some security teams
    //  prefer 404 for another user's resource so the response does not confirm
    //  that order 17 exists at all. Both are defensible; pick one, apply it
    //  everywhere, and be able to justify it (LO4).
    // ------------------------------------------------------------------------
    private static void EnsureCanAccess(Order order, long requestingUserId, bool callerIsStaffOrAdmin)
    {
        if (callerIsStaffOrAdmin) return;               // staff handle all orders
        if (order.CustomerId == requestingUserId) return;

        throw new ForbiddenException("This order belongs to another customer.");
    }

    private static OrderDto ToDto(Order o)
        => new(
            o.Id,
            o.CustomerId,
            o.Status,
            o.PaymentMethod,
            o.Items.Select(i => new OrderLineDto(
                i.ProductId,
                i.Product?.Name ?? "(product)",
                i.Quantity,
                i.UnitPrice,
                i.LineTotal)).ToList(),
            o.Subtotal,
            o.DeliveryFee,
            o.Surcharge,
            o.Total,
            o.CreatedAt);
}
