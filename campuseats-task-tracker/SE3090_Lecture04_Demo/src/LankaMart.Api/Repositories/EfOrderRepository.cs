using LankaMart.Api.Data;
using LankaMart.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace LankaMart.Api.Repositories;

public sealed class EfOrderRepository : IOrderRepository
{
    private readonly AppDbContext _db;
    public EfOrderRepository(AppDbContext db) => _db = db;

    /// Two levels of Include: the order, its items, and each item's product -
    /// so the response can show product names without a query per line.
    public Task<Order?> GetByIdWithItemsAsync(long id, CancellationToken ct = default)
        => _db.Orders
              .Include(o => o.Items).ThenInclude(i => i.Product)
              .AsNoTracking()
              .FirstOrDefaultAsync(o => o.Id == id, ct);

    public Task<Order?> GetForUpdateAsync(long id, CancellationToken ct = default)
        => _db.Orders
              .Include(o => o.Items)
              .FirstOrDefaultAsync(o => o.Id == id, ct);

    /// SLIDE 20 - the query that justifies idx_orders_customer. Run
    /// EXPLAIN ANALYZE on it with and without the index (db/useful_queries.sql)
    /// and show the class Index Scan versus Seq Scan.
    public async Task<IReadOnlyList<Order>> GetByCustomerAsync(
        long customerId, CancellationToken ct = default)
        => await _db.Orders
                    .Include(o => o.Items).ThenInclude(i => i.Product)
                    .AsNoTracking()
                    .Where(o => o.CustomerId == customerId)
                    .OrderByDescending(o => o.CreatedAt)
                    .ToListAsync(ct);

    public async Task<(IReadOnlyList<Order> Items, int TotalItems)> SearchAsync(
        string? status, int page, int pageSize, CancellationToken ct = default)
    {
        var query = _db.Orders
                       .Include(o => o.Items)
                       .Include(o => o.Customer)
                       .AsNoTracking();

        if (!string.IsNullOrWhiteSpace(status))
            query = query.Where(o => o.Status == status.ToUpper());

        var total = await query.CountAsync(ct);

        var items = await query.OrderByDescending(o => o.Id)
                               .Skip((page - 1) * pageSize)
                               .Take(pageSize)
                               .ToListAsync(ct);

        return (items, total);
    }

    public async Task<Order> AddAsync(Order order, CancellationToken ct = default)
    {
        // Adding the order also adds its Items - EF Core walks the graph and
        // fills in order_id on each child row after the parent gets its id.
        await _db.Orders.AddAsync(order, ct);
        return order;
    }
}
