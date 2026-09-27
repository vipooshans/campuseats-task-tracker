using System.ComponentModel.DataAnnotations;

namespace LankaMart.Api.Dtos;

// ============================================================================
//  THE MOST IMPORTANT DTO IN THIS PROJECT, for one thing it does not contain.
//
//  There is no CustomerId.
//
//  SLIDE 33, last row: "Trusting client claims". If the request said whose
//  order this is, any authenticated customer could place - or later read -
//  orders belonging to someone else by editing one number in the JSON. The
//  customer id comes from the 'sub' claim of the verified JWT, server-side,
//  and from nowhere else.
//
//  Prove it live: send { "customerId": 1, "items": [...] } while logged in as
//  someone else. The field is ignored, and the order belongs to the token.
// ============================================================================
public record CreateOrderDto(
    [Required, MinLength(1)] List<OrderItemInputDto> Items,

    [Required, RegularExpression("^(card|cash)$",
        ErrorMessage = "PaymentMethod must be 'card' or 'cash'.")]
    string PaymentMethod);

/// Nested DTOs are validated too - MVC walks the whole object graph, so a
/// quantity of 0 inside the array is rejected with 400 before any action runs.
public record OrderItemInputDto(
    [Range(1, long.MaxValue)] long ProductId,
    [Range(1, 100)]           int  Quantity);

public record OrderDto(
    long                       Id,
    long                       CustomerId,
    string                     Status,
    string                     PaymentMethod,
    IReadOnlyList<OrderLineDto> Items,
    decimal                    Subtotal,
    decimal                    DeliveryFee,
    decimal                    Surcharge,
    decimal                    Total,
    DateTime                   CreatedAt);

public record OrderLineDto(
    long    ProductId,
    string  ProductName,
    int     Quantity,
    decimal UnitPrice,       // the SNAPSHOT, not today's price (SLIDE 15)
    decimal LineTotal);
