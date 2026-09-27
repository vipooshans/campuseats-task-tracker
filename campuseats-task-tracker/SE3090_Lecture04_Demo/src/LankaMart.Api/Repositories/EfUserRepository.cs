using LankaMart.Api.Data;
using LankaMart.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace LankaMart.Api.Repositories;

public sealed class EfUserRepository : IUserRepository
{
    private readonly AppDbContext _db;
    public EfUserRepository(AppDbContext db) => _db = db;

    // ------------------------------------------------------------------------
    //  SLIDE 31 step 2 - the login query.
    //
    //  Watch the SQL this produces in the console (EF Core logging is on in
    //  Development): one SELECT with joins to user_roles, roles,
    //  role_permissions and permissions. Without the Includes, the token
    //  service would ask for roles later and fire one query per collection -
    //  the N+1 problem from SLIDE 25.
    //
    //  ToLower() on both sides makes the comparison case-insensitive, because
    //  "Amal@mail.lk" and "amal@mail.lk" are the same mailbox to every mail
    //  server on earth. Note the trade-off honestly in class: applying a
    //  function to the column means the plain ux_users_email index cannot be
    //  used. Production options are storing a normalised copy of the address,
    //  the citext extension, or an expression index on lower(email).
    // ------------------------------------------------------------------------
    public Task<User?> GetByEmailWithRolesAsync(string email, CancellationToken ct = default)
        => _db.Users
              .Include(u => u.UserRoles).ThenInclude(ur => ur.Role)
                  .ThenInclude(r => r!.RolePermissions).ThenInclude(rp => rp.Permission)
              .AsNoTracking()
              .FirstOrDefaultAsync(u => u.Email.ToLower() == email.ToLower(), ct);

    public Task<User?> GetByIdWithRolesAsync(long id, CancellationToken ct = default)
        => _db.Users
              .Include(u => u.UserRoles).ThenInclude(ur => ur.Role)
                  .ThenInclude(r => r!.RolePermissions).ThenInclude(rp => rp.Permission)
              .AsNoTracking()
              .FirstOrDefaultAsync(u => u.Id == id, ct);

    public Task<User?> GetForUpdateAsync(long id, CancellationToken ct = default)
        => _db.Users
              .Include(u => u.UserRoles)
              .FirstOrDefaultAsync(u => u.Id == id, ct);

    /// A pre-flight check so registration can answer 409 with a clear message.
    /// The UNIQUE index is still the real guarantee: two requests arriving in
    /// the same millisecond both pass this check and one of them loses at the
    /// index (SLIDE 26 - PostgreSQL error 23505, mapped to 409).
    public Task<bool> ExistsWithEmailAsync(string email, CancellationToken ct = default)
        => _db.Users.AnyAsync(u => u.Email.ToLower() == email.ToLower(), ct);

    public async Task<User> AddAsync(User user, CancellationToken ct = default)
    {
        await _db.Users.AddAsync(user, ct);
        return user;                 // Id is populated after SaveChangesAsync
    }

    public async Task<(IReadOnlyList<User> Items, int TotalItems)> SearchAsync(
        string? search, int page, int pageSize, CancellationToken ct = default)
    {
        var query = _db.Users
                       .Include(u => u.UserRoles).ThenInclude(ur => ur.Role)
                       .AsNoTracking();

        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(u => EF.Functions.ILike(u.Name,  $"%{search}%")
                                  || EF.Functions.ILike(u.Email, $"%{search}%"));

        var total = await query.CountAsync(ct);

        var items = await query.OrderBy(u => u.Id)
                               .Skip((page - 1) * pageSize)
                               .Take(pageSize)
                               .ToListAsync(ct);

        return (items, total);
    }
}
