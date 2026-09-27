namespace LankaMart.Api.Models;

// ============================================================
//  SLIDE 18 - normalization, made concrete.
//
//  In the Lecture 03 demo, Product had a Category STRING. That is
//  the flat spreadsheet on the left of slide 18: "Accessories"
//  repeated on every row, one typo away from two categories.
//
//  Here the fact lives exactly once, in this table, and products
//  point at it with a foreign key. Renaming a category is now a
//  one-row UPDATE instead of mass string surgery.
// ============================================================
public class Category
{
    public long   Id   { get; set; }
    public string Name { get; set; } = "";

    public ICollection<Product> Products { get; set; } = new List<Product>();
}
