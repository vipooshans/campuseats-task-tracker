namespace LankaMart.Api.Models;

// ============================================================
//  SLIDE 15 + 37 - the "users" table.
//
//  Two details that carry the whole security lesson:
//
//   1. There is no Password property. There is a PasswordHash.
//      Not even the database administrator can read a password
//      out of this table (SLIDE 20).
//
//   2. Email is a NATURAL key: we keep it UNIQUE but we do NOT
//      make it the primary key, because people change email
//      addresses and a primary key must never change (SLIDE 14).
// ============================================================
public class User
{
    public long   Id           { get; set; }
    public string Name         { get; set; } = "";
    public string Email        { get; set; } = "";

    /// The bcrypt output, e.g. "$2a$12$Nn3...". Never a password.
    public string PasswordHash { get; set; } = "";

    /// Lets an admin disable an account without deleting order history.
    public bool     IsActive  { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // --- Navigation properties: the C# side of the foreign keys ---------
    public ICollection<UserRole>     UserRoles     { get; set; } = new List<UserRole>();
    public ICollection<Order>        Orders        { get; set; } = new List<Order>();
    public ICollection<RefreshToken> RefreshTokens { get; set; } = new List<RefreshToken>();

    /// Convenience for the token service. Roles must be loaded first
    /// (.Include) or this returns an empty list - see the N+1 demo.
    public IEnumerable<string> RoleNames()
        => UserRoles.Where(ur => ur.Role is not null).Select(ur => ur.Role!.Name);
}
