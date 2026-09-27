namespace LankaMart.Api.Repositories;

// ============================================================================
//  SLIDE 24, speaker notes:
//    "Where should a database transaction live when creating an order with
//     items?  Answer: the SERVICE layer - it owns the unit of work spanning
//     multiple repository calls."
//
//  This interface is that answer, written down. It lets OrderService say
//  "these three writes succeed together or not at all" without importing a
//  single EF Core type - so the service still runs in the unit tests against
//  a fake, with no database anywhere.
//
//  The A in ACID (SLIDE 8) is not a slogan; this is where you get it.
// ============================================================================
public interface IUnitOfWork
{
    /// One round trip. EF Core works out which INSERT / UPDATE / DELETE
    /// statements are needed from its change tracker (SLIDE 25).
    Task<int> SaveChangesAsync(CancellationToken ct = default);

    Task<IAppTransaction> BeginTransactionAsync(CancellationToken ct = default);
}

/// Deliberately not IDbContextTransaction: that type belongs to EF Core, and
/// leaking it into the service layer would drag the ORM into every unit test.
public interface IAppTransaction : IAsyncDisposable
{
    Task CommitAsync(CancellationToken ct = default);
    Task RollbackAsync(CancellationToken ct = default);
}
