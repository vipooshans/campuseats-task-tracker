namespace LankaMart.Api.Models;

// ============================================================
//  SLIDE 36 - the finer-grained option. A role becomes a BUNDLE
//  of permission codes, so a new job title is a new bundle
//  rather than a new set of if-statements in the code.
//
//  Codes look like "products.create", "orders.refund".
// ============================================================
public class Permission
{
    public long   Id   { get; set; }
    public string Code { get; set; } = "";

    public string? Description { get; set; }

    public ICollection<RolePermission> RolePermissions { get; set; } = new List<RolePermission>();
}
