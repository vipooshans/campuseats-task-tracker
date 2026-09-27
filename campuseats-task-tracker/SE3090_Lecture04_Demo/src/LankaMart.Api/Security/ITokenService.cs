using LankaMart.Api.Models;

namespace LankaMart.Api.Security;

/// SLIDE 30-32 - minting the two tokens, and hashing the refresh token
/// before it is allowed anywhere near the database.
public interface ITokenService
{
    /// Signs a short-lived JWT carrying the user's id, roles and permissions.
    AccessToken CreateAccessToken(User user, IEnumerable<string> roles, IEnumerable<string> permissions);

    /// Generates a cryptographically random refresh token. The RAW value is
    /// returned to the caller exactly once; only its hash may be stored.
    string CreateRefreshTokenValue();

    /// SHA-256, hex encoded. Fast on purpose: unlike a password, a refresh
    /// token is 256 bits of randomness, so there is nothing to brute-force
    /// and no need for a slow hash.
    string HashRefreshToken(string rawToken);
}

/// The token plus the metadata a client needs in order to know when to refresh.
public record AccessToken(string Value, DateTime ExpiresAtUtc, int ExpiresInSeconds);
