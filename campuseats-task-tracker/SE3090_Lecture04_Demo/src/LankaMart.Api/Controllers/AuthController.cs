using System.IdentityModel.Tokens.Jwt;
using LankaMart.Api.Common;
using LankaMart.Api.Dtos;
using LankaMart.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LankaMart.Api.Controllers;

// ============================================================================
//  SLIDE 29 - "Public: POST /auth/register, POST /auth/login".
//
//  These two endpoints MUST be anonymous: you cannot require a token from
//  someone whose entire purpose is to obtain one. Everything else in the API
//  is protected, which is why the default policy in Program.cs requires
//  authentication and these actions opt out explicitly with [AllowAnonymous].
//
//  Deny by default, allow by exception. The opposite - protect by remembering
//  to add [Authorize] to each new controller - fails the first time somebody
//  is in a hurry.
// ============================================================================
[ApiController]
[Route("api/v1/auth")]
[Produces("application/json")]
public class AuthController : ControllerBase
{
    private readonly IAuthService _auth;

    public AuthController(IAuthService auth) => _auth = auth;

    /// <summary>Create a customer account and return a token pair.</summary>
    /// <remarks>
    /// Self-registration always grants exactly the Customer role. Adding
    /// "role": "Admin" to the request body has no effect - RegisterRequest has
    /// no such field, so there is nowhere for it to bind (SLIDE 40,
    /// mass assignment).
    /// </remarks>
    [HttpPost("register")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<AuthResponse>> Register(
        [FromBody] RegisterRequest request, CancellationToken ct)
    {
        var result = await _auth.RegisterAsync(request, ct);
        return StatusCode(StatusCodes.Status201Created, result);
    }

    /// <summary>Exchange email + password for an access token and a refresh token.</summary>
    /// <remarks>
    /// SLIDE 31 steps 1-4. A wrong password and an unknown email produce the
    /// SAME 401 and the same message, on purpose: different answers let an
    /// attacker discover which addresses are registered.
    /// </remarks>
    [HttpPost("login")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<AuthResponse>> Login(
        [FromBody] LoginRequest request, CancellationToken ct)
        => Ok(await _auth.LoginAsync(request, ct));

    /// <summary>Trade a refresh token for a fresh pair. The old one dies here.</summary>
    /// <remarks>
    /// SLIDE 32. Rotation: each refresh token is single-use. Present a revoked
    /// one and the API assumes theft and revokes the whole family - try it in
    /// LankaMart.Api.http, the request is already written for you.
    /// </remarks>
    [HttpPost("refresh")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<AuthResponse>> Refresh(
        [FromBody] RefreshRequest request, CancellationToken ct)
        => Ok(await _auth.RefreshAsync(request.RefreshToken, ct));

    /// <summary>Revoke a refresh token server-side.</summary>
    /// <remarks>
    /// Always 204, whether or not the token existed - an endpoint that answers
    /// 404 for unknown tokens is a free test oracle for stolen values.
    /// Remember: the ACCESS token keeps working until it expires. Clearing
    /// React state is not a logout on its own.
    /// </remarks>
    [HttpPost("logout")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Logout([FromBody] LogoutRequest request, CancellationToken ct)
    {
        await _auth.LogoutAsync(request.RefreshToken, ct);
        return NoContent();
    }

    /// <summary>Show what the JWT middleware extracted from your token.</summary>
    /// <remarks>
    /// SLIDE 31 step 6 made visible. Nothing in this action reads the database:
    /// the identity below came entirely from the signed token. That is the
    /// stateless benefit - and the reason a revoked role lingers until refresh.
    /// </remarks>
    [HttpGet("me")]
    [Authorize]
    [ProducesResponseType(typeof(MeResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public ActionResult<MeResponse> Me()
    {
        var expClaim = User.FindFirst(JwtRegisteredClaimNames.Exp)?.Value;

        DateTime? expiresAt = long.TryParse(expClaim, out var unixSeconds)
            ? DateTimeOffset.FromUnixTimeSeconds(unixSeconds).UtcDateTime
            : null;

        return Ok(new MeResponse(
            User.RequireUserId(),
            User.FindFirst("name")?.Value ?? "",
            User.GetEmail(),
            User.GetRoles(),
            User.GetPermissions(),
            expiresAt));
    }
}
