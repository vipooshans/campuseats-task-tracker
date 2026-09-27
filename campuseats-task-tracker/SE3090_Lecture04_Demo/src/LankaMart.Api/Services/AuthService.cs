using LankaMart.Api.Common;
using LankaMart.Api.Dtos;
using LankaMart.Api.Models;
using LankaMart.Api.Repositories;
using LankaMart.Api.Security;

namespace LankaMart.Api.Services;

// ============================================================================
//  PART 4 OF THE LECTURE, IMPLEMENTED.   (SLIDES 31, 32, 33)
//
//  Read the four public methods in order and you have walked the whole flow
//  from slide 31, plus the rotation and theft-detection rules from slide 32.
//
//  What is NOT in this file: HTTP. No status codes, no headers, no cookies.
//  The service throws UnauthorizedException and GlobalExceptionHandler turns
//  that into 401. That separation is why every rule below is unit-tested in
//  LankaMart.ServiceTests without starting a server or a database.
// ============================================================================
public sealed class AuthService : IAuthService
{
    private readonly IUserRepository         _users;
    private readonly IRoleRepository         _roles;
    private readonly IRefreshTokenRepository _refreshTokens;
    private readonly IUnitOfWork             _uow;
    private readonly IPasswordHasher         _hasher;
    private readonly ITokenService           _tokens;
    private readonly IClock                  _clock;
    private readonly JwtOptionsSnapshot      _jwt;
    private readonly ILogger<AuthService>    _logger;

    public AuthService(
        IUserRepository users,
        IRoleRepository roles,
        IRefreshTokenRepository refreshTokens,
        IUnitOfWork uow,
        IPasswordHasher hasher,
        ITokenService tokens,
        IClock clock,
        JwtOptionsSnapshot jwt,
        ILogger<AuthService> logger)
    {
        _users         = users;
        _roles         = roles;
        _refreshTokens = refreshTokens;
        _uow           = uow;
        _hasher        = hasher;
        _tokens        = tokens;
        _clock         = clock;
        _jwt           = jwt;
        _logger        = logger;
    }

    // ========================================================================
    //  REGISTER
    // ========================================================================
    public async Task<AuthResponse> RegisterAsync(
        RegisterRequest request, CancellationToken ct = default)
    {
        var email = request.Email.Trim().ToLowerInvariant();

        // A friendly pre-flight check. The UNIQUE index on users.email is the
        // real guarantee - two simultaneous requests both pass this line and
        // one of them loses at the index, producing PostgreSQL 23505, which
        // GlobalExceptionHandler also maps to 409 (SLIDE 26). Belt and braces,
        // on purpose: this is defence in depth applied to a race condition.
        if (await _users.ExistsWithEmailAsync(email, ct))
            throw new ConflictException($"The email '{email}' is already registered.");

        // SLIDE 36 - least privilege. Self-registration grants exactly one
        // role: Customer. Nothing in the request body can influence this;
        // becoming Staff requires an Admin action.
        var customerRole = await _roles.GetByNameAsync(Roles.Customer, ct)
            ?? throw new InvalidOperationException(
                "The 'Customer' role is missing. Seed the roles table before registering users.");

        // Two writes that must both succeed: the user, and their role
        // assignment. A user with no role could log in and see nothing, so we
        // wrap them in one transaction (SLIDE 8 - atomicity).
        await using var tx = await _uow.BeginTransactionAsync(ct);

        var user = new User
        {
            Name  = request.Name.Trim(),
            Email = email,

            // The ONLY place a plaintext password is ever touched, and it is
            // gone by the end of this line (SLIDE 20, 31).
            PasswordHash = _hasher.Hash(request.Password),
            CreatedAt    = _clock.UtcNow
        };

        await _users.AddAsync(user, ct);
        await _uow.SaveChangesAsync(ct);          // user.Id is assigned here

        _roles.AddUserRole(new UserRole
        {
            UserId     = user.Id,
            RoleId     = customerRole.Id,
            AssignedAt = _clock.UtcNow
        });

        var issued = await IssueTokenPairAsync(
            user,
            new[] { Roles.Customer },
            RolePermissionCodes(customerRole),
            familyId: Guid.NewGuid(),
            ct: ct);

        await _uow.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        _logger.LogInformation("Registered user {UserId} ({Email})", user.Id, user.Email);

        return issued;
    }

    // ========================================================================
    //  LOGIN                                       (SLIDE 31, steps 1 to 4)
    // ========================================================================
    public async Task<AuthResponse> LoginAsync(
        LoginRequest request, CancellationToken ct = default)
    {
        var email = request.Email.Trim().ToLowerInvariant();

        // Step 2: SELECT ... WHERE email = $1, fast because of ux_users_email.
        var user = await _users.GetByEmailWithRolesAsync(email, ct);

        if (user is null)
        {
            // SLIDE 31, speaker notes: an attacker must not be able to tell
            // "no such user" from "wrong password", or they can enumerate which
            // email addresses are registered - useful for phishing and for
            // credential-stuffing lists.
            //
            // The identical MESSAGE is the obvious half. This hash call is the
            // half students miss: without it, an unknown email would answer in
            // 2 ms while a real one takes ~300 ms of bcrypt, and that timing
            // difference leaks exactly the same fact. So we spend the same time.
            _hasher.Verify(request.Password, DummyHash);

            _logger.LogWarning("Failed login: no account for {Email}", email);
            throw new UnauthorizedException();
        }

        // Step 3: verify against the stored hash. Note the argument order -
        // (plaintext, storedHash). Reversing it silently fails every login.
        if (!_hasher.Verify(request.Password, user.PasswordHash))
        {
            _logger.LogWarning("Failed login: wrong password for user {UserId}", user.Id);
            throw new UnauthorizedException();          // same message, deliberately
        }

        if (!user.IsActive)
        {
            // Also the same generic message: "your account is disabled" tells a
            // stranger that the account exists. The real reason goes in the log,
            // where support can read it (SLIDE 41 - two audiences).
            _logger.LogWarning("Failed login: account {UserId} is disabled", user.Id);
            throw new UnauthorizedException();
        }

        var roles       = user.RoleNames().ToList();
        var permissions = UserPermissionCodes(user);

        // A new login starts a NEW token family (SLIDE 32).
        var response = await IssueTokenPairAsync(
            user, roles, permissions, familyId: Guid.NewGuid(), ct: ct);

        await _uow.SaveChangesAsync(ct);

        _logger.LogInformation(
            "User {UserId} logged in with roles {Roles}", user.Id, string.Join(",", roles));

        return response;
    }

    // ========================================================================
    //  REFRESH                          (SLIDE 32 - rotation + theft detection)
    // ========================================================================
    public async Task<AuthResponse> RefreshAsync(
        string refreshToken, CancellationToken ct = default)
    {
        var now  = _clock.UtcNow;
        var hash = _tokens.HashRefreshToken(refreshToken);

        var stored = await _refreshTokens.GetByHashAsync(hash, ct);

        if (stored is null)
        {
            _logger.LogWarning("Refresh attempted with an unknown token");
            throw new UnauthorizedException("Invalid refresh token.");
        }

        // ------------------------------------------------------------------
        //  THEFT DETECTION - the part worth five minutes on the projector.
        //
        //  Rotation means every refresh token is single-use: using it revokes
        //  it and issues a replacement. So a token that is ALREADY revoked and
        //  is being presented again means one of two things - the real user
        //  replayed an old token, or somebody stole one. We cannot tell which,
        //  so we assume the worse case and revoke the entire family, forcing a
        //  fresh login. An attacker who steals a token therefore gets, at
        //  most, one use before both parties are locked out and the incident
        //  is visible in the logs.
        // ------------------------------------------------------------------
        if (stored.RevokedAt is not null)
        {
            var revoked = await _refreshTokens.RevokeFamilyAsync(
                stored.FamilyId, now, "reuse-detected", ct);
            await _uow.SaveChangesAsync(ct);

            _logger.LogWarning(
                "Refresh token REUSE detected for user {UserId}. Revoked {Count} token(s) in family {FamilyId}",
                stored.UserId, revoked, stored.FamilyId);

            throw new UnauthorizedException("Invalid refresh token.");
        }

        if (stored.ExpiresAt <= now)
        {
            _logger.LogInformation("Refresh token expired for user {UserId}", stored.UserId);
            throw new UnauthorizedException("Refresh token has expired. Please log in again.");
        }

        var user = await _users.GetByIdWithRolesAsync(stored.UserId, ct);
        if (user is null || !user.IsActive)
            throw new UnauthorizedException("Invalid refresh token.");

        // Rotate: the presented token dies here...
        stored.RevokedAt     = now;
        stored.RevokedReason = "rotated";

        // ...and its replacement inherits the family, so the chain stays linked.
        var response = await IssueTokenPairAsync(
            user, user.RoleNames().ToList(), UserPermissionCodes(user),
            familyId: stored.FamilyId, ct: ct);

        stored.ReplacedByTokenHash = _tokens.HashRefreshToken(response.RefreshToken);

        await _uow.SaveChangesAsync(ct);

        _logger.LogInformation("Rotated refresh token for user {UserId}", user.Id);

        return response;
    }

    // ========================================================================
    //  LOGOUT
    // ========================================================================
    public async Task LogoutAsync(string refreshToken, CancellationToken ct = default)
    {
        var hash   = _tokens.HashRefreshToken(refreshToken);
        var stored = await _refreshTokens.GetByHashAsync(hash, ct);

        // Deliberately silent about whether the token existed. Logout returns
        // 204 either way: an endpoint that answers 404 for unknown tokens is a
        // free oracle for testing stolen values.
        if (stored is not null && stored.RevokedAt is null)
        {
            stored.RevokedAt     = _clock.UtcNow;
            stored.RevokedReason = "logout";
            await _uow.SaveChangesAsync(ct);

            _logger.LogInformation("User {UserId} logged out", stored.UserId);
        }

        // SLIDE 32 - the ACCESS token is still valid until it expires. Nothing
        // here can un-sign it. That is the honest cost of stateless auth, and
        // the reason its lifetime is 15 minutes rather than 30 days.
    }

    // ========================================================================
    //  Shared helper: mint one access token + one refresh token.
    // ========================================================================
    private async Task<AuthResponse> IssueTokenPairAsync(
        User user,
        IReadOnlyList<string> roles,
        IReadOnlyList<string> permissions,
        Guid familyId,
        CancellationToken ct)
    {
        var access = _tokens.CreateAccessToken(user, roles, permissions);

        var rawRefresh = _tokens.CreateRefreshTokenValue();

        await _refreshTokens.AddAsync(new RefreshToken
        {
            UserId    = user.Id,

            // Only the hash is persisted (SLIDE 32).
            TokenHash = _tokens.HashRefreshToken(rawRefresh),
            FamilyId  = familyId,
            CreatedAt = _clock.UtcNow,
            ExpiresAt = _clock.UtcNow.AddDays(_jwt.RefreshTokenDays)
        }, ct);

        return new AuthResponse(
            AccessToken:  access.Value,
            ExpiresIn:    access.ExpiresInSeconds,
            ExpiresAtUtc: access.ExpiresAtUtc,
            RefreshToken: rawRefresh,          // returned once, never stored
            TokenType:    "Bearer",
            User: new UserSummaryDto(user.Id, user.Name, user.Email, roles));
    }

    private static IReadOnlyList<string> UserPermissionCodes(User user)
        => user.UserRoles
               .Where(ur => ur.Role is not null)
               .SelectMany(ur => ur.Role!.RolePermissions)
               .Where(rp => rp.Permission is not null)
               .Select(rp => rp.Permission!.Code)
               .Distinct()
               .ToList();

    private static IReadOnlyList<string> RolePermissionCodes(Role role)
        => role.RolePermissions
               .Where(rp => rp.Permission is not null)
               .Select(rp => rp.Permission!.Code)
               .Distinct()
               .ToList();

    // Computed once per process, not per request: bcrypt at work factor 12
    // costs a few hundred milliseconds, which is the entire point of it.
    private static string? _dummyHash;
    private string DummyHash => _dummyHash ??= _hasher.Hash("timing-equalisation-only");
}

/// A tiny value holder so AuthService does not need IOptions<JwtOptions> just
/// to read one number - and so the tests can set it in one line.
public sealed class JwtOptionsSnapshot
{
    public int RefreshTokenDays { get; init; } = 7;
}
