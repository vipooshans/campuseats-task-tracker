namespace LankaMart.Api.Models;

// ============================================================
//  SLIDE 15 + 20 - the junction table, and the nuance students
//  argue about most.
//
//  orders <-> products is MANY-TO-MANY. A relational database
//  cannot store that directly, so order_items resolves it, with a
//  COMPOSITE primary key (order_id, product_id). That key also
//  prevents the same product appearing twice in one order.
//
//  UnitPrice looks like redundancy - products already has price -
//  but it is not. It is a HISTORICAL SNAPSHOT. products.price is
//  "the price now"; order_items.unit_price is "the price when
//  this was sold". If we always joined to products.price instead,
//  tomorrow's price change would silently rewrite every past
//  invoice (SLIDE 15, SLIDE 21 Q3).
// ============================================================
public class OrderItem
{
    public long OrderId   { get; set; }
    public long ProductId { get; set; }

    public Order?   Order   { get; set; }
    public Product? Product { get; set; }

    public int     Quantity  { get; set; }
    public decimal UnitPrice { get; set; }

    /// Ignored by EF Core - computed, not stored.
    public decimal LineTotal => Quantity * UnitPrice;
}
