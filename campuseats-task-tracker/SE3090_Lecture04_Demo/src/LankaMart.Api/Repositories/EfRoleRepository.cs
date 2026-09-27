using LankaMart.Api.Data;
using LankaMart.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace LankaMart.Api.Repositories;

public sealed class EfRoleRepository : IRoleRepository
{
    private readonly AppDbContext _db;
    public EfRoleRepository(AppDbContext db) => _db = db;

    public Task<Role?> GetByNameAsync(string name, CancellationToken ct = default)
        => _db.Roles.FirstOrDefaultAsync(r => r.Name == name, ct);

    public async Task<IReadOnlyList<Role>> GetAllAsync(CancellationToken ct = default)
        => await _db.Roles.AsNoTracking().OrderBy(r => r.Id).ToListAsync(ct);

    /// SLIDE 37 - inserting one row into the junction table IS the act of
    /// granting a role. No column on users changes.
    public void AddUserRole(UserRole userRole) => _db.UserRoles.Add(userRole);

    public void RemoveUserRole(UserRole userRole) => _db.UserRoles.Remove(userRole);

    public Task<UserRole?> FindUserRoleAsync(long userId, long roleId, CancellationToken ct = default)
        => _db.UserRoles.FirstOrDefaultAsync(ur => ur.UserId == userId && ur.RoleId == roleId, ct);
}
