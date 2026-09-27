using LankaMart.Api.Data;
using LankaMart.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace LankaMart.Api.Repositories;

public sealed class EfCategoryRepository : ICategoryRepository
{
    private readonly AppDbContext _db;
    public EfCategoryRepository(AppDbContext db) => _db = db;

    public async Task<IReadOnlyList<Category>> GetAllAsync(CancellationToken ct = default)
        => await _db.Categories.AsNoTracking().OrderBy(c => c.Name).ToListAsync(ct);

    public Task<Category?> GetByIdAsync(long id, CancellationToken ct = default)
        => _db.Categories.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, ct);
}
