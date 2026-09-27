using LankaMart.Api.Data;
using LankaMart.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace LankaMart.Api.Repositories;

// ============================================================================
//  SLIDE 25 - "LINQ queries are translated into parameterized SQL."
//
//  This is the class to have open when you say that. Turn on the SQL log
//  (already on in Development) and every method below prints the statement
//  PostgreSQL actually ran, with $1, $2 placeholders where the values went.
//  Those placeholders are why LINQ is immune to the injection attack on
//  SLIDE 40: the values never touch the SQL text.
// ============================================================================
public sealed class EfProductRepository : IProductRepository
{
    private readonly AppDbContext _db;
    public EfProductRepository(AppDbContext db) => _db = db;

    public async Task<(IReadOnlyList<Product> Items, int TotalItems)> SearchAsync(
        long? categoryId, string? search, int page, int pageSize, CancellationToken ct = default)
    {
        // Nothing has been executed yet. LINQ builds an expression tree; the
        // SQL is sent only when we await Count/ToList below. This is why the
        // filters can be composed with ordinary if-statements.
        var query = _db.Products.Include(p => p.Category).AsNoTracking();

        if (categoryId is not null)
            query = query.Where(p => p.CategoryId == categoryId);

        if (!string.IsNullOrWhiteSpace(search))
            // ILIKE is PostgreSQL's case-insensitive LIKE. EF.Functions exposes
            // provider-specific SQL while keeping the value parameterized.
            // (Caveat for the curious: % and _ inside `search` are still
            //  wildcards. Harmless here; escape them if a user could DoS you
            //  with '%%%%%'.)
            query = query.Where(p => EF.Functions.ILike(p.Name, $"%{search}%"));

        // TWO round trips, on purpose: PostgreSQL counts the matching rows,
        // then returns only the requested page. The alternative - fetching
        // everything and counting in C# - is what we did with the in-memory
        // repository last lecture and must not survive contact with a database.
        var total = await query.CountAsync(ct);

        var items = await query.OrderBy(p => p.Name)
                               .Skip((page - 1) * pageSize)
                               .Take(pageSize)              // -> LIMIT / OFFSET
                               .ToListAsync(ct);

        return (items, total);
    }

    public Task<Product?> GetByIdAsync(long id, CancellationToken ct = default)
        => _db.Products.Include(p => p.Category).AsNoTracking()
              .FirstOrDefaultAsync(p => p.Id == id, ct);

    /// Tracked: EF Core keeps a snapshot, so changing a property and calling
    /// SaveChangesAsync produces an UPDATE with only the changed columns.
    public Task<Product?> GetForUpdateAsync(long id, CancellationToken ct = default)
        => _db.Products.Include(p => p.Category)
              .FirstOrDefaultAsync(p => p.Id == id, ct);

    public async Task<IReadOnlyList<Product>> GetForUpdateAsync(
        IEnumerable<long> ids, CancellationToken ct = default)
    {
        var idList = ids.Distinct().ToList();

        // One query with WHERE id = ANY($1) for the whole basket.
        return await _db.Products.Where(p => idList.Contains(p.Id)).ToListAsync(ct);
    }

    public async Task<Product> AddAsync(Product product, CancellationToken ct = default)
    {
        await _db.Products.AddAsync(product, ct);
        return product;
    }

    public void Remove(Product product) => _db.Products.Remove(product);

    public Task<bool> ExistsWithNameAsync(
        string name, long? excludingId = null, CancellationToken ct = default)
        => _db.Products.AnyAsync(
               p => p.Name.ToLower() == name.ToLower()
                    && (excludingId == null || p.Id != excludingId), ct);
}
