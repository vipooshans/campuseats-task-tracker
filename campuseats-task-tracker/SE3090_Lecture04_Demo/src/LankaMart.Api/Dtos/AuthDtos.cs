using System.ComponentModel.DataAnnotations;

namespace LankaMart.Api.Dtos;

// ============================================================================
//  SLIDE 40 - "Validate on BOTH sides, for different reasons."
//  These attributes are the BACKEND gate: the real one, the one an attacker
//  with curl cannot skip. React's identical rules are UX, not security.
//
//  GOTCHA REPEATED FROM LECTURE 03, because it costs an hour every time:
//  on a record, a validation attribute goes on the CONSTRUCTOR PARAMETER, as
//  below. Writing [property: Required] moves it to the generated property
//  where MVC cannot see it, and the rule silently never runs.
// ============================================================================

/// POST /api/v1/auth/register
///
/// SLIDE 40 - note what is NOT here: no Role, no IsActive, no Id. This is
/// protection against MASS ASSIGNMENT (over-posting). A client that adds
/// "role": "Admin" to the JSON body is simply ignored, because there is
/// nowhere for that value to land. Binding straight to the User entity - which
/// does have those fields - is how privilege-escalation bugs get shipped.
public record RegisterRequest(
    [Required, StringLength(120, MinimumLength = 2)] string Name,

    [Required, EmailAddress, StringLength(200)]      string Email,

    // Length is a floor, not a policy. Real systems also check the password
    // against a breached-password list (see OWASP Password Storage).
    [Required, StringLength(100, MinimumLength = 8)] string Password);

/// POST /api/v1/auth/login
public record LoginRequest(
    [Required, EmailAddress] string Email,
    [Required]               string Password);

/// POST /api/v1/auth/refresh - SLIDE 32, the silent renewal.
public record RefreshRequest([Required] string RefreshToken);

/// POST /api/v1/auth/logout - revokes the refresh token server-side.
/// SLIDE 32: clearing client state alone is not a logout; the refresh token
/// stays valid for days unless the server is told about it.
public record LogoutRequest([Required] string RefreshToken);

/// What a successful login or refresh returns.
///
/// SLIDE 32 - in a production React app the refresh token would be delivered
/// in an HttpOnly, Secure, SameSite cookie so JavaScript cannot read it, and
/// only the access token would live in memory. It is returned in the body here
/// so the class can SEE both tokens in Swagger, decode them, and compare their
/// lifetimes. Say that trade-off out loud rather than copying this shape into
/// production code.
public record AuthResponse(
    string          AccessToken,
    int             ExpiresIn,        // seconds - what an axios interceptor uses
    DateTime        ExpiresAtUtc,
    string          RefreshToken,
    string          TokenType,        // "Bearer"
    UserSummaryDto  User);

public record UserSummaryDto(
    long                 Id,
    string               Name,
    string               Email,
    IReadOnlyList<string> Roles);

/// GET /api/v1/auth/me - proves what the middleware extracted from the token.
public record MeResponse(
    long                  Id,
    string                Name,
    string?               Email,
    IReadOnlyList<string> Roles,
    IReadOnlyList<string> Permissions,
    DateTime?             TokenExpiresAtUtc);
