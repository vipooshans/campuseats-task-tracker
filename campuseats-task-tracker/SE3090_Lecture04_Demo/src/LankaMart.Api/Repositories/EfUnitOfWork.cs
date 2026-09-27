using LankaMart.Api.Data;
using Microsoft.EntityFrameworkCore.Storage;

namespace LankaMart.Api.Repositories;

/// The EF Core side of IUnitOfWork. Note how thin it is: the DbContext already
/// IS a unit of work, so this class exists only to keep EF Core out of the
/// service layer (SLIDE 24).
public sealed class EfUnitOfWork : IUnitOfWork
{
    private readonly AppDbContext _db;

    public EfUnitOfWork(AppDbContext db) => _db = db;

    public Task<int> SaveChangesAsync(CancellationToken ct = default)
        => _db.SaveChangesAsync(ct);

    public async Task<IAppTransaction> BeginTransactionAsync(CancellationToken ct = default)
        => new EfTransaction(await _db.Database.BeginTransactionAsync(ct));

    private sealed class EfTransaction : IAppTransaction
    {
        private readonly IDbContextTransaction _tx;
        public EfTransaction(IDbContextTransaction tx) => _tx = tx;

        public Task CommitAsync(CancellationToken ct = default)   => _tx.CommitAsync(ct);
        public Task RollbackAsync(CancellationToken ct = default) => _tx.RollbackAsync(ct);

        /// Disposing an uncommitted transaction rolls it back. That is why the
        /// `await using` in OrderService is not decoration: if any statement
        /// throws, PostgreSQL never sees a COMMIT and the whole attempt vanishes.
        public ValueTask DisposeAsync() => _tx.DisposeAsync();
    }
}
