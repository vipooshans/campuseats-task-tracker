namespace LankaMart.Api.Models;

// ============================================================
//  SLIDE 15 - the "orders" table, and the 1:N relationship that
//  matters most in this schema.
//
//  ONE customer places MANY orders, so the foreign key lives on
//  the MANY side: orders.customer_id. Putting an order_id column
//  on users - or worse, a comma-separated list of order ids -
//  is the classic beginner error (SLIDE 13).
//
//  ON DELETE RESTRICT protects history: deleting a customer who
//  has orders must fail loudly rather than silently erase
//  invoices (SLIDE 14).
// ============================================================
public class Order
{
    public long  Id         { get; set; }

    /// SLIDE 36 + 42 - this column IS the object-level authorization check.
    /// "Customers see only their own orders" is enforced by comparing this
    /// value with the 'sub' claim in the caller's JWT. Role checks alone
    /// cannot express it.
    public long  CustomerId { get; set; }
    public User? Customer   { get; set; }

    /// PENDING -> CONFIRMED -> DELIVERED, or CANCELLED / REFUNDED.
    /// Private setter: state changes go through the methods below, so an
    /// illegal transition cannot be written from outside the class.
    public string Status { get; private set; } = OrderStatus.Pending;

    public string   PaymentMethod { get; set; } = "card";
    public decimal  DeliveryFee   { get; set; }
    public decimal  Surcharge     { get; set; }
    public DateTime CreatedAt     { get; set; } = DateTime.UtcNow;

    public ICollection<OrderItem> Items { get; set; } = new List<OrderItem>();

    // --- Computed, never stored (mapped as Ignore in AppDbContext) -------
    //  We deliberately do NOT keep a total_amount column. Storing a total
    //  is denormalization: legitimate when measured and documented (SLIDE 17),
    //  unnecessary at this scale, and one more thing to keep in sync.
    public decimal Subtotal => Items.Sum(i => i.LineTotal);
    public decimal Total    => Subtotal + DeliveryFee + Surcharge;

    public void Confirm() => Status = OrderStatus.Confirmed;

    public void Cancel()
    {
        if (Status == OrderStatus.Cancelled)
            throw new InvalidOperationException("Order is already cancelled");
        if (Status == OrderStatus.Delivered)
            throw new InvalidOperationException("A delivered order cannot be cancelled");
        Status = OrderStatus.Cancelled;
    }

    public void Refund() => Status = OrderStatus.Refunded;
}

/// Kept as constants rather than an enum so the stored value is readable
/// in psql and pgAdmin. A CHECK constraint in the database restricts the
/// column to exactly these five strings - see AppDbContext.
public static class OrderStatus
{
    public const string Pending   = "PENDING";
    public const string Confirmed = "CONFIRMED";
    public const string Delivered = "DELIVERED";
    public const string Cancelled = "CANCELLED";
    public const string Refunded  = "REFUNDED";

    public static readonly string[] All =
        { Pending, Confirmed, Delivered, Cancelled, Refunded };
}
