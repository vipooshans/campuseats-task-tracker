using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using LankaMart.Api.Common;
using LankaMart.Api.Models;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace LankaMart.Api.Security;

// ============================================================================
//  SLIDE 30 - "header.payload.signature".
//
//  Everything below builds the PAYLOAD and then signs it. Run the API, log in,
//  copy the accessToken out of the response and paste it into jwt.io: you will
//  see the exact claim set written here, in plain readable JSON, with no
//  password required. That is the lesson - a JWT is SIGNED, not ENCRYPTED.
//  Anyone can read it; only the holder of the signing key can forge one.
//
//  Consequently: ids, roles and expiry go in. Passwords, NIC numbers, salary
//  and anything else you would not print on a boarding pass stay out.
// ============================================================================
public sealed class JwtTokenService : ITokenService
{
    private readonly JwtOptions _options;
    private readonly IClock     _clock;

    public JwtTokenService(IOptions<JwtOptions> options, IClock clock)
    {
        _options = options.Value;
        _clock   = clock;
    }

    public AccessToken CreateAccessToken(
        User user, IEnumerable<string> roles, IEnumerable<string> permissions)
    {
        var now     = _clock.UtcNow;
        var expires = now.AddMinutes(_options.AccessTokenMinutes);

        // --- The payload (SLIDE 30) ---------------------------------------
        var claims = new List<Claim>
        {
            // "sub" = subject = WHO this token is about. The ownership checks
            // in OrderService compare this against orders.customer_id.
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),

            new("email", user.Email),
            new("name",  user.Name),

            // "jti" = a unique id for this token. Not used here, but it is what
            // a deny-list implementation would key on if you ever need instant
            // revocation of access tokens.
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        // SLIDE 37 - "At login: load the user's roles -> embed as claims in the
        // JWT -> no DB lookup on later requests." That is the stateless win,
        // and also the cost: a role revoked right now only takes effect at the
        // next refresh. Short access tokens are what keep that window small.
        foreach (var role in roles.Distinct())
            claims.Add(new Claim(ClaimsPrincipalExtensions.RoleClaim, role));

        foreach (var permission in permissions.Distinct())
            claims.Add(new Claim(ClaimsPrincipalExtensions.PermissionClaim, permission));

        // --- The signature (SLIDE 30, 33) --------------------------------
        var key         = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.SigningKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer:             _options.Issuer,
            audience:           _options.Audience,
            claims:             claims,
            notBefore:          now,
            expires:            expires,        // the "exp" claim
            signingCredentials: credentials);

        var value = new JwtSecurityTokenHandler().WriteToken(token);

        return new AccessToken(
            value,
            expires,
            (int)Math.Round((expires - now).TotalSeconds));
    }

    // ------------------------------------------------------------------------
    //  SLIDE 32 - the refresh token.
    //
    //  Not a JWT. It carries no claims and needs none: it is a 256-bit random
    //  string whose only job is to be looked up in refresh_tokens. Randomness
    //  from RandomNumberGenerator, never from Random or Guid.NewGuid() -
    //  Random is predictable from its seed and Guids are not designed to be
    //  unguessable.
    // ------------------------------------------------------------------------
    public string CreateRefreshTokenValue()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);   // 256 bits
        return Base64UrlEncoder.Encode(bytes);           // URL/header safe
    }

    public string HashRefreshToken(string rawToken)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(rawToken));
        return Convert.ToHexString(hash);                 // uppercase hex
    }
}
