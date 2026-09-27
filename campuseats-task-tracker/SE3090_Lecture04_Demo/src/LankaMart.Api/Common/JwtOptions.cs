using System.ComponentModel.DataAnnotations;

namespace LankaMart.Api.Common;

// ============================================================
//  SLIDE 30, 32, 33 - every JWT decision that should be
//  configuration rather than a magic number in the code.
// ============================================================
public class JwtOptions
{
    public const string SectionName = "Jwt";

    /// Who issued the token. Validated on every request, so a token minted by
    /// a different system cannot be replayed here.
    [Required] public string Issuer { get; set; } = "LankaMart.Api";

    /// Who the token is for. Same reasoning.
    [Required] public string Audience { get; set; } = "LankaMart.Client";

    /// SLIDE 33, "Weak / leaked secret".
    ///
    /// HMAC-SHA256 needs at least 256 bits = 32 bytes of key material, and
    /// the library will refuse a shorter one (error IDX10653). A short or
    /// dictionary key can be brute-forced offline in seconds, after which the
    /// attacker mints their own Admin token and every check in this
    /// application politely agrees with them.
    ///
    /// This value must arrive from the environment, never from a committed
    /// file. Program.cs refuses to start outside Development if the key is
    /// missing or still set to the shared development default.
    [Required, MinLength(32)] public string SigningKey { get; set; } = "";

    /// SLIDE 32 - short, because a stolen access token cannot be revoked.
    /// The expiry IS the damage limiter.
    [Range(1, 1440)] public int AccessTokenMinutes { get; set; } = 15;

    /// Long-lived but revocable, because it is stored (hashed) in the database.
    [Range(1, 365)] public int RefreshTokenDays { get; set; } = 7;
}
