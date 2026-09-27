using LankaMart.Api.Models;
using LankaMart.Api.Repositories;
using LankaMart.Api.Security;

namespace LankaMart.ServiceTests;

// ============================================================================
//  SLIDE 24 - the payoff for depending on interfaces.
//
//  Every class below is an ordinary C# object with a Dictionary inside. No
//  PostgreSQL, no EF Core, no Docker, no web server, no mocking library. The
//  services cannot tell the difference, because all they ever knew was the
//  interface.
//
//  This is what lets the security rules in AuthService and OrderService be
//  tested in milliseconds - which in turn is what makes it realistic to run
//  them on every commit in Lecture 08's CI pipeline.
// ============================================================================

public class FakeUserRepository : IUserRepository
{
    private readonly Dictionary<long, User> _users = new();
    private long _nextId;

    public FakeUserRepository(params User[] seed)
    {
        foreach (var u in seed)
        {
            if (u.Id == 0) u.Id = ++_nextId; else _nextId = Math.Max(_nextId, u.Id);
            _users[u.Id] = u;
        }
    }

    public Task<User?> GetByEmailWithRolesAsync(string email, CancellationToken ct = default)
        => Task.FromResult(_users.Values.FirstOrDefault(
               u => string.Equals(u.Email, email, StringComparison.OrdinalIgnoreCase)));

    public Task<User?> GetByIdWithRolesAsync(long id, CancellationToken ct = default)
        => Task.FromResult(_users.TryGetValue(id, out var u) ? u : null);

    public Task<User?> GetForUpdateAsync(long id, CancellationToken ct = default)
        => GetByIdWithRolesAsync(id, ct);

    public Task<bool> ExistsWithEmailAsync(string email, CancellationToken ct = default)
        => Task.FromResult(_users.Values.Any(
               u => string.Equals(u.Email, email, StringComparison.OrdinalIgnoreCase)));

    public Task<User> AddAsync(User user, CancellationToken ct = default)
    {
        user.Id = ++_nextId;              // the database would do this
        _users[user.Id] = user;
        return Task.FromResult(user);
    }

    public Task<(IReadOnlyList<User> Items, int TotalItems)> SearchAsync(
        string? search, int page, int pageSize, CancellationToken ct = default)
    {
        var all = _users.Values.OrderBy(u => u.Id).ToList();
        var pageItems = all.Skip((page - 1) * pageSize).Take(pageSize).ToList();
        return Task.FromResult<(IReadOnlyList<User>, int)>((pageItems, all.Count));
    }
}

public class FakeRoleRepository : IRoleRepository
{
    private readonly List<Role>     _roles = new();
    private readonly List<UserRole> _userRoles = new();

    public IReadOnlyList<UserRole> Assignments => _userRoles;

    public FakeRoleRepository(params Role[] seed)
    {
        long id = 0;
        foreach (var r in seed)
        {
            if (r.Id == 0) r.Id = ++id;
            _roles.Add(r);
        }
    }

    public Task<Role?> GetByNameAsync(string name, CancellationToken ct = default)
        => Task.FromResult(_roles.FirstOrDefault(r => r.Name == name));

    public Task<IReadOnlyList<Role>> GetAllAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<Role>>(_roles);

    public void AddUserRole(UserRole userRole) => _userRoles.Add(userRole);

    public void RemoveUserRole(UserRole userRole) => _userRoles.Remove(userRole);

    public Task<UserRole?> FindUserRoleAsync(long userId, long roleId, CancellationToken ct = default)
        => Task.FromResult(_userRoles.FirstOrDefault(
               ur => ur.UserId == userId && ur.RoleId == roleId));
}

public class FakeCategoryRepository : ICategoryRepository
{
    private readonly Dictionary<long, Category> _items = new();

    public FakeCategoryRepository(params Category[] seed)
    {
        long id = 0;
        foreach (var c in seed)
        {
            if (c.Id == 0) c.Id = ++id;
            _items[c.Id] = c;
        }
    }

    public Task<IReadOnlyList<Category>> GetAllAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<Category>>(_items.Values.ToList());

    public Task<Category?> GetByIdAsync(long id, CancellationToken ct = default)
        => Task.FromResult(_items.TryGetValue(id, out var c) ? c : null);
}

public class FakeProductRepository : IProductRepository
{
    private readonly Dictionary<long, Product> _items = new();
    private long _nextId;

    public FakeProductRepository(params Product[] seed)
    {
        foreach (var p in seed)
        {
            p.Id = ++_nextId;
            _items[p.Id] = p;
        }
    }

    public Task<(IReadOnlyList<Product> Items, int TotalItems)> SearchAsync(
        long? categoryId, string? search, int page, int pageSize, CancellationToken ct = default)
    {
        var query = _items.Values.AsEnumerable();

        if (categoryId is not null) query = query.Where(p => p.CategoryId == categoryId);

        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(p => p.Name.Contains(search, StringComparison.OrdinalIgnoreCase));

        var all = query.OrderBy(p => p.Name).ToList();
        var pageItems = all.Skip((page - 1) * pageSize).Take(pageSize).ToList();

        return Task.FromResult<(IReadOnlyList<Product>, int)>((pageItems, all.Count));
    }

    public Task<Product?> GetByIdAsync(long id, CancellationToken ct = default)
        => Task.FromResult(_items.TryGetValue(id, out var p) ? p : null);

    public Task<Product?> GetForUpdateAsync(long id, CancellationToken ct = default)
        => GetByIdAsync(id, ct);

    public Task<IReadOnlyList<Product>> GetForUpdateAsync(
        IEnumerable<long> ids, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<Product>>(
               ids.Distinct().Where(_items.ContainsKey).Select(id => _items[id]).ToList());

    public Task<Product> AddAsync(Product product, CancellationToken ct = default)
    {
        product.Id = ++_nextId;
        _items[product.Id] = product;
        return Task.FromResult(product);
    }

    public void Remove(Product product) => _items.Remove(product.Id);

    public Task<bool> ExistsWithNameAsync(
        string name, long? excludingId = null, CancellationToken ct = default)
        => Task.FromResult(_items.Values.Any(
               p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase)
                    && (excludingId is null || p.Id != excludingId)));
}

public class FakeOrderRepository : IOrderRepository
{
    private readonly Dictionary<long, Order> _items = new();
    private long _nextId;

    public FakeOrderRepository(params Order[] seed)
    {
        foreach (var o in seed)
        {
            o.Id = ++_nextId;
            _items[o.Id] = o;
        }
    }

    public Task<Order?> GetByIdWithItemsAsync(long id, CancellationToken ct = default)
        => Task.FromResult(_items.TryGetValue(id, out var o) ? o : null);

    public Task<Order?> GetForUpdateAsync(long id, CancellationToken ct = default)
        => GetByIdWithItemsAsync(id, ct);

    public Task<IReadOnlyList<Order>> GetByCustomerAsync(
        long customerId, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<Order>>(
               _items.Values.Where(o => o.CustomerId == customerId).ToList());

    public Task<(IReadOnlyList<Order> Items, int TotalItems)> SearchAsync(
        string? status, int page, int pageSize, CancellationToken ct = default)
    {
        var all = _items.Values
            .Where(o => status is null || o.Status == status.ToUpper())
            .OrderByDescending(o => o.Id).ToList();

        var pageItems = all.Skip((page - 1) * pageSize).Take(pageSize).ToList();
        return Task.FromResult<(IReadOnlyList<Order>, int)>((pageItems, all.Count));
    }

    public Task<Order> AddAsync(Order order, CancellationToken ct = default)
    {
        order.Id = ++_nextId;
        _items[order.Id] = order;
        return Task.FromResult(order);
    }
}

public class FakeRefreshTokenRepository : IRefreshTokenRepository
{
    private readonly List<RefreshToken> _tokens = new();
    private long _nextId;

    public IReadOnlyList<RefreshToken> All => _tokens;

    public Task<RefreshToken?> GetByHashAsync(string tokenHash, CancellationToken ct = default)
        => Task.FromResult(_tokens.FirstOrDefault(t => t.TokenHash == tokenHash));

    public Task<RefreshToken> AddAsync(RefreshToken token, CancellationToken ct = default)
    {
        token.Id = ++_nextId;
        _tokens.Add(token);
        return Task.FromResult(token);
    }

    public Task<int> RevokeFamilyAsync(
        Guid familyId, DateTime revokedAtUtc, string reason, CancellationToken ct = default)
    {
        var affected = _tokens.Where(t => t.FamilyId == familyId && t.RevokedAt is null).ToList();

        foreach (var token in affected)
        {
            token.RevokedAt     = revokedAtUtc;
            token.RevokedReason = reason;
        }

        return Task.FromResult(affected.Count);
    }

    public Task<int> RevokeAllForUserAsync(
        long userId, DateTime revokedAtUtc, string reason, CancellationToken ct = default)
    {
        var affected = _tokens.Where(t => t.UserId == userId && t.RevokedAt is null).ToList();

        foreach (var token in affected)
        {
            token.RevokedAt     = revokedAtUtc;
            token.RevokedReason = reason;
        }

        return Task.FromResult(affected.Count);
    }
}

/// The fake unit of work does nothing, and that is correct: the fake
/// repositories already hold the objects, so there is nothing to flush. What
/// matters is that the SERVICE still calls SaveChanges and still opens a
/// transaction - the shape of the code under test is unchanged.
public class FakeUnitOfWork : IUnitOfWork
{
    public int SaveCount { get; private set; }
    public int TransactionCount { get; private set; }
    public bool Committed { get; private set; }

    public Task<int> SaveChangesAsync(CancellationToken ct = default)
    {
        SaveCount++;
        return Task.FromResult(0);
    }

    public Task<IAppTransaction> BeginTransactionAsync(CancellationToken ct = default)
    {
        TransactionCount++;
        return Task.FromResult<IAppTransaction>(new FakeTransaction(this));
    }

    private sealed class FakeTransaction : IAppTransaction
    {
        private readonly FakeUnitOfWork _owner;
        public FakeTransaction(FakeUnitOfWork owner) => _owner = owner;

        public Task CommitAsync(CancellationToken ct = default)
        {
            _owner.Committed = true;
            return Task.CompletedTask;
        }

        public Task RollbackAsync(CancellationToken ct = default) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}

/// Real bcrypt costs a few hundred milliseconds per hash - deliberately. In a
/// test suite that is pure waiting, so we swap in a fast fake. The behaviour
/// being tested ("was the password stored in the clear?", "is the message the
/// same for both failure modes?") does not depend on the algorithm.
public class FakePasswordHasher : IPasswordHasher
{
    public const string Prefix = "hashed::";

    public string Hash(string password) => Prefix + password;

    public bool Verify(string password, string hash) => hash == Prefix + password;
}

/// SLIDE 32 - lets a test say "eight days later" without waiting eight days.
public class FixedClock : IClock
{
    public FixedClock(DateTime utcNow) => UtcNow = utcNow;

    public DateTime UtcNow { get; set; }

    public void Advance(TimeSpan by) => UtcNow = UtcNow.Add(by);
}
