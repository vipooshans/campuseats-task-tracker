namespace LankaMart.Api.Common;

/// Carried over from Lecture 03 (slide 35): a list response also carries the
/// paging metadata the client needs to build a pager.
///
/// One thing changed: TotalItems now comes from a SQL COUNT(*) executed by
/// PostgreSQL, not from .Count on an in-memory list. See EfProductRepository.
public record PagedResult<T>(
    IReadOnlyList<T> Items,
    int Page,
    int PageSize,
    int TotalItems)
{
    public int TotalPages => PageSize <= 0 ? 0 : (int)Math.Ceiling(TotalItems / (double)PageSize);
}
