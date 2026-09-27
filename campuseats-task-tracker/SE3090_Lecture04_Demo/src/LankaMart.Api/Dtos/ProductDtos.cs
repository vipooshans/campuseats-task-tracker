using System.ComponentModel.DataAnnotations;

namespace LankaMart.Api.Dtos;

// ============================================================================
//  SLIDE 39 (Lecture 03) meets SLIDE 18 (this lecture).
//
//  The schema normalised: products now hold a category_id foreign key instead
//  of a repeated category string. The DTO is where that becomes a design
//  choice rather than a leak:
//
//    * requests carry CategoryId, because a foreign key must reference a real
//      row and the client should not be inventing category names;
//    * responses carry BOTH the id and the resolved name, so the React table
//      needs no second call.
//
//  Also still absent: SupplierCostNote. The entity has it, the API never
//  shows it.
// ============================================================================

public record ProductDto(
    long    Id,
    string  Name,
    long    CategoryId,
    string  Category,        // resolved from the join - the client wants a label
    decimal Price,
    int     StockQty,
    bool    InStock);        // computed, not stored

public record CreateProductDto(
    [Required, StringLength(120, MinimumLength = 2)] string  Name,
    [Range(1, long.MaxValue)]                       long    CategoryId,
    [Range(0, 1_000_000)]                           decimal Price,
    [Range(0, int.MaxValue)]                        int     StockQty);

/// PUT = FULL REPLACEMENT. Everything omitted is genuinely erased, which is
/// why every field is required.
public record ReplaceProductDto(
    [Required, StringLength(120, MinimumLength = 2)] string  Name,
    [Range(1, long.MaxValue)]                       long    CategoryId,
    [Range(0, 1_000_000)]                           decimal Price,
    [Range(0, int.MaxValue)]                        int     StockQty);

/// PATCH = PARTIAL UPDATE. null means "leave this field alone".
public record PatchProductDto(
    [StringLength(120, MinimumLength = 2)] string?  Name,
    [Range(1, long.MaxValue)]              long?    CategoryId,
    [Range(0, 1_000_000)]                  decimal? Price,
    [Range(0, int.MaxValue)]               int?     StockQty);

public record CategoryDto(long Id, string Name, int ProductCount);
