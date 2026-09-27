using LankaMart.Api.Models;

namespace LankaMart.Api.Repositories;

// ============================================================================
//  SLIDE 24 - the REPOSITORY layer: data access only, no decisions.
//
//  HONEST NOTE ON WHAT CHANGED SINCE LECTURE 03.
//  Last lecture IProductRepository had GetAllAsync() and the service filtered
//  the list in C#. That works for eleven in-memory products and is a disaster
//  against a real table: it downloads every row over the network to throw most
//  of them away. So SearchAsync now takes the filters and the paging, and the
//  EF Core implementation turns them into a WHERE, a COUNT(*) and a
//  LIMIT/OFFSET that PostgreSQL executes.
//
//  The PATTERN is unchanged - services still depend on these interfaces and
//  never on EF Core - but the interface itself had to learn that the data is
//  now remote. Worth saying out loud in class: "swap the implementation" is
//  real, and it is not always free.
//
//  Two method flavours appear throughout, and the distinction is SLIDE 25's
//  change tracking:
//     Get...Async         -> AsNoTracking, for reads. Faster, no snapshots.
//     Get...ForUpdateAsync -> tracked, so mutating the object and calling
//                             SaveChangesAsync writes an UPDATE.
// ============================================================================

public interface IUserRepository
{
    /// The login lookup (SLIDE 31 step 2). Uses the UNIQUE index on email and
    /// eager-loads roles + permissions, because the token needs them
    /// immediately - one JOIN instead of the N+1 pattern.
    Task<User?> GetByEmailWithRolesAsync(string email, CancellationToken ct = default);

    Task<User?> GetByIdWithRolesAsync(long id, CancellationToken ct = default);
    Task<User?> GetForUpdateAsync(long id, CancellationToken ct = default);
    Task<bool>  ExistsWithEmailAsync(string email, CancellationToken ct = default);
    Task<User>  AddAsync(User user, CancellationToken ct = default);

    Task<(IReadOnlyList<User> Items, int TotalItems)> SearchAsync(
        string? search, int page, int pageSize, CancellationToken ct = default);
}

public interface IRoleRepository
{
    Task<Role?> GetByNameAsync(string name, CancellationToken ct = default);
    Task<IReadOnlyList<Role>> GetAllAsync(CancellationToken ct = default);
    void AddUserRole(UserRole userRole);
    void RemoveUserRole(UserRole userRole);
    Task<UserRole?> FindUserRoleAsync(long userId, long roleId, CancellationToken ct = default);
}

public interface ICategoryRepository
{
    Task<IReadOnlyList<Category>> GetAllAsync(CancellationToken ct = default);
    Task<Category?> GetByIdAsync(long id, CancellationToken ct = default);
}

public interface IProductRepository
{
    Task<(IReadOnlyList<Product> Items, int TotalItems)> SearchAsync(
        long? categoryId, string? search, int page, int pageSize, CancellationToken ct = default);

    Task<Product?> GetByIdAsync(long id, CancellationToken ct = default);
    Task<Product?> GetForUpdateAsync(long id, CancellationToken ct = default);

    /// Tracked, and loaded in ONE query for the whole basket. Placing an order
    /// touching five products must not fire five SELECTs (SLIDE 25, N+1).
    Task<IReadOnlyList<Product>> GetForUpdateAsync(IEnumerable<long> ids, CancellationToken ct = default);

    Task<Product> AddAsync(Product product, CancellationToken ct = default);
    void Remove(Product product);
    Task<bool> ExistsWithNameAsync(string name, long? excludingId = null, CancellationToken ct = default);
}

public interface IOrderRepository
{
    Task<Order?> GetByIdWithItemsAsync(long id, CancellationToken ct = default);
    Task<Order?> GetForUpdateAsync(long id, CancellationToken ct = default);
    Task<IReadOnlyList<Order>> GetByCustomerAsync(long customerId, CancellationToken ct = default);

    Task<(IReadOnlyList<Order> Items, int TotalItems)> SearchAsync(
        string? status, int page, int pageSize, CancellationToken ct = default);

    Task<Order> AddAsync(Order order, CancellationToken ct = default);
}

public interface IRefreshTokenRepository
{
    /// Looks up by HASH - the raw token is never stored, so it cannot be
    /// searched for (SLIDE 32).
    Task<RefreshToken?> GetByHashAsync(string tokenHash, CancellationToken ct = default);

    Task<RefreshToken> AddAsync(RefreshToken token, CancellationToken ct = default);

    /// Theft response: revoke every token descended from one login.
    Task<int> RevokeFamilyAsync(Guid familyId, DateTime revokedAtUtc, string reason,
                                CancellationToken ct = default);

    Task<int> RevokeAllForUserAsync(long userId, DateTime revokedAtUtc, string reason,
                                    CancellationToken ct = default);
}
