namespace LankaMart.Api.Models;

// ============================================================
//  SLIDE 37 - "Same pattern twice!"
//
//  users <-> roles is MANY-TO-MANY: a user can be both Staff and
//  Customer (employees shop too), and a role has many users.
//  A relational database cannot store M:N directly, so we resolve
//  it with a junction table - exactly like order_items in Part 2.
//
//  The primary key is COMPOSITE: (user_id, role_id). That single
//  choice also guarantees the same role cannot be assigned twice.
// ============================================================
public class UserRole
{
    public long UserId { get; set; }
    public long RoleId { get; set; }

    public User? User { get; set; }
    public Role? Role { get; set; }

    /// Audit trail: who granted this role and when. A junction table
    /// growing its own attributes is a sign it is a real entity (SLIDE 13).
    public DateTime AssignedAt { get; set; } = DateTime.UtcNow;
}
