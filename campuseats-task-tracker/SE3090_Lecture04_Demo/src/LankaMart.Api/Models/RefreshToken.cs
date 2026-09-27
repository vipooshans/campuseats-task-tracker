namespace LankaMart.Api.Models;

// ============================================================
//  SLIDE 32 - the table that resolves the central trade-off of
//  token authentication.
//
//  We want stateless verification (fast, scalable, no database
//  lookup per request) AND the ability to revoke (logout, stolen
//  account, banned user). One token cannot do both, so we use two:
//
//    * access token  - short-lived JWT, never stored anywhere
//    * refresh token - long-lived, stored HERE, therefore revocable
//
//  Note what is stored: TokenHash, not the token. If this table
//  leaks, the attacker still cannot present a valid refresh token,
//  for the same reason a leaked users table does not leak
//  passwords. Hash everything that behaves like a credential.
//
//  FamilyId enables THEFT DETECTION. Rotation means each refresh
//  issues a new token and revokes the old one, so a token should
//  only ever be used once. If a revoked token is presented again,
//  either the user replayed it or someone stole it - we cannot
//  tell, so we assume theft and revoke the entire family.
// ============================================================
public class RefreshToken
{
    public long  Id     { get; set; }
    public long  UserId { get; set; }
    public User? User   { get; set; }

    /// SHA-256 of the raw token, hex encoded. The raw value is returned to
    /// the client exactly once and never persisted.
    public string TokenHash { get; set; } = "";

    /// All tokens descended from one login share this id.
    public Guid FamilyId { get; set; }

    public DateTime  CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime  ExpiresAt { get; set; }
    public DateTime? RevokedAt { get; set; }

    /// Set when this token is rotated, so the chain can be walked in an audit.
    public string? ReplacedByTokenHash { get; set; }

    /// "rotated", "logout", "reuse-detected", "admin-revoked".
    public string? RevokedReason { get; set; }

    /// A method, not a property, so EF Core does not try to map it.
    public bool IsUsable(DateTime utcNow) => RevokedAt is null && ExpiresAt > utcNow;
}
