namespace LankaMart.Api.Models;

// ============================================================
//  SLIDE 15 + 20 - the "products" table.
//
//  Money is decimal in C# and NUMERIC(10,2) in PostgreSQL.
//  NEVER double / FLOAT: 0.1 + 0.2 != 0.3, and after 400,000
//  invoice lines the missing cents become an audit finding
//  (SLIDE 19).
//
//  StockQty carries CHECK (stock_qty >= 0) in the database. That
//  constraint catches an overselling bug even if three layers of
//  application code miss it - constraints are the last line of
//  defence in depth (SLIDE 39).
// ============================================================
public class Product
{
    public long    Id       { get; set; }
    public string  Name     { get; set; } = "";
    public decimal Price    { get; set; }          // LKR
    public int     StockQty { get; set; }

    /// Foreign key to categories. NOT NULL, because in LankaMart every
    /// product must sit in a category - a mandatory relationship
    /// (SLIDE 14: "forgetting NOT NULL on a mandatory FK is a classic").
    public long      CategoryId { get; set; }
    public Category? Category   { get; set; }

    /// INTERNAL ONLY. Kept from Lecture 03 as the reminder of why DTOs
    /// exist: if a controller returned this entity directly, our supplier
    /// margins would ship to the browser.
    public string SupplierCostNote { get; set; } = "";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<OrderItem> OrderItems { get; set; } = new List<OrderItem>();

    public bool CanFulfil(int qty) => StockQty >= qty;
}
