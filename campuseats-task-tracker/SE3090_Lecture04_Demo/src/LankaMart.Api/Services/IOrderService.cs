using LankaMart.Api.Common;
using LankaMart.Api.Dtos;

namespace LankaMart.Api.Services;

// ============================================================================
//  Look closely at these signatures - the design decision is in them.
//
//  Every method that touches somebody's data takes the CALLER's identity as
//  ordinary parameters: requestingUserId and callerIsStaffOrAdmin. The service
//  does not receive HttpContext, ClaimsPrincipal or the token.
//
//  Why: authorization decisions belong to the layer that owns the business
//  rules, but the mechanics of HTTP identity do not. Passing two plain values
//  keeps the rule testable ("a customer cannot read another customer's order"
//  is a unit test, not an integration test) and keeps the service reusable from
//  a background job or a CLI, which have no HttpContext at all.
// ============================================================================
public interface IOrderService
{
    Task<OrderDto> PlaceOrderAsync(long customerId, CreateOrderDto dto, CancellationToken ct = default);

    Task<OrderDto> GetByIdAsync(
        long orderId, long requestingUserId, bool callerIsStaffOrAdmin, CancellationToken ct = default);

    Task<IReadOnlyList<OrderDto>> GetMyOrdersAsync(long customerId, CancellationToken ct = default);

    Task<PagedResult<OrderDto>> SearchAsync(
        string? status, int page, int pageSize, CancellationToken ct = default);

    Task<OrderDto> CancelAsync(
        long orderId, long requestingUserId, bool callerIsStaffOrAdmin, CancellationToken ct = default);

    Task<OrderDto> RefundAsync(long orderId, CancellationToken ct = default);
}
