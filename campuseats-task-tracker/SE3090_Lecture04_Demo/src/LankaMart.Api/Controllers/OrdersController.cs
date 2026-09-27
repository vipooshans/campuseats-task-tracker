using LankaMart.Api.Common;
using LankaMart.Api.Dtos;
using LankaMart.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LankaMart.Api.Controllers;

// ============================================================================
//  WHERE ROLES STOP BEING ENOUGH.                     (SLIDE 36, 38, 42)
//
//  Every action here is [Authorize]d, so the caller is a known user. That
//  settles "is this a customer?" and settles nothing about "is this THEIR
//  order?". The second question needs the row, so the check moves into the
//  service - see EnsureCanAccess in OrderService.
//
//  The classroom demo: log in as two different customers, place an order with
//  each, then ask for the other's order id. Same role, same endpoint, same
//  valid token - 403.
// ============================================================================
[ApiController]
[Route("api/v1/orders")]
[Produces("application/json")]
[Authorize]                       // authentication required for everything below
public class OrdersController : ControllerBase
{
    private readonly IOrderService _service;

    public OrdersController(IOrderService service) => _service = service;

    /// <summary>Place an order for the logged-in customer.</summary>
    /// <remarks>
    /// The customer id comes from the 'sub' claim of your token. The request
    /// body cannot influence it - there is no CustomerId field to bind.
    /// </remarks>
    [HttpPost]
    [ProducesResponseType(typeof(OrderDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<OrderDto>> Place(
        [FromBody] CreateOrderDto dto, CancellationToken ct)
    {
        var order = await _service.PlaceOrderAsync(User.RequireUserId(), dto, ct);
        return CreatedAtAction(nameof(GetOrderById), new { id = order.Id }, order);
    }

    /// <summary>Your own orders.</summary>
    /// <remarks>
    /// No id in the route, and that is the safest possible design: the query
    /// filters on the token's user id, so this endpoint is structurally
    /// incapable of returning someone else's data.
    /// </remarks>
    [HttpGet("my")]
    [ProducesResponseType(typeof(IReadOnlyList<OrderDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<OrderDto>>> MyOrders(CancellationToken ct)
        => Ok(await _service.GetMyOrdersAsync(User.RequireUserId(), ct));

    /// <summary>One order by id - yours, unless you are Staff or Admin.</summary>
    /// <remarks>OWASP API Security #1 lives here. See OrderService.EnsureCanAccess.</remarks>
    [HttpGet("{id:long}", Name = nameof(GetOrderById))]
    [ProducesResponseType(typeof(OrderDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<OrderDto>> GetOrderById(long id, CancellationToken ct)
        => Ok(await _service.GetByIdAsync(id, User.RequireUserId(), User.IsStaffOrAdmin(), ct));

    /// <summary>Every order in the system. Staff or Admin.</summary>
    [HttpGet]
    [Authorize(Roles = Roles.StaffOrAdmin)]
    [ProducesResponseType(typeof(PagedResult<OrderDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<PagedResult<OrderDto>>> Search(
        [FromQuery] string? status,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
        => Ok(await _service.SearchAsync(status, page, pageSize, ct));

    /// <summary>Cancel an order and return the stock. Owner, Staff or Admin.</summary>
    [HttpPost("{id:long}/cancel")]
    [ProducesResponseType(typeof(OrderDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<OrderDto>> Cancel(long id, CancellationToken ct)
        => Ok(await _service.CancelAsync(id, User.RequireUserId(), User.IsStaffOrAdmin(), ct));

    /// <summary>Refund an order. Requires the orders.refund PERMISSION.</summary>
    /// <remarks>
    /// SLIDE 38, bottom note: a POLICY instead of a role list. The endpoint
    /// says what capability is needed, not who has it, so tomorrow's
    /// "Finance Officer" role can be given orders.refund without editing this
    /// file. Staff have the permission in the seed data; Customers do not.
    /// </remarks>
    [HttpPost("{id:long}/refund")]
    [Authorize(Policy = Policies.CanRefundOrders)]
    [ProducesResponseType(typeof(OrderDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<OrderDto>> Refund(long id, CancellationToken ct)
        => Ok(await _service.RefundAsync(id, ct));
}
