namespace LankaMart.Api.Models;

/// SLIDE 37 - the second junction table. roles <-> permissions is
/// also M:N, resolved the same way, with a composite primary key.
public class RolePermission
{
    public long RoleId       { get; set; }
    public long PermissionId { get; set; }

    public Role?       Role       { get; set; }
    public Permission? Permission { get; set; }
}
