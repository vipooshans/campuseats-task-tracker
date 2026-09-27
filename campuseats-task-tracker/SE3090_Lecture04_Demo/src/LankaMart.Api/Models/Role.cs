namespace LankaMart.Api.Models;

// ============================================================
//  SLIDE 36 + 37 - RBAC: permissions attach to ROLES, and users
//  gain permissions by being assigned roles. Three rows in this
//  table govern three thousand users.
//
//  Seeded values: Admin, Staff, Customer.
// ============================================================
public class Role
{
    public long   Id   { get; set; }
    public string Name { get; set; } = "";

    public string? Description { get; set; }

    public ICollection<UserRole>       UserRoles       { get; set; } = new List<UserRole>();
    public ICollection<RolePermission> RolePermissions { get; set; } = new List<RolePermission>();
}
