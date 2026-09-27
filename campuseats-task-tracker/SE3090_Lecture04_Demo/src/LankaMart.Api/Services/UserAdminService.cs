using LankaMart.Api.Common;
using LankaMart.Api.Dtos;
using LankaMart.Api.Models;
using LankaMart.Api.Repositories;
using LankaMart.Api.Security;
using Microsoft.Extensions.Options;

namespace LankaMart.Api.Services;

public sealed class UserAdminService : IUserAdminService
{
    private readonly IUserRepository           _users;
    private readonly IRoleRepository           _roles;
    private readonly IRefreshTokenRepository   _refreshTokens;
    private readonly IUnitOfWork               _uow;
    private readonly IClock                    _clock;
    private readonly LankaMartOptions          _options;
    private readonly ILogger<UserAdminService> _logger;

    public UserAdminService(
        IUserRepository users,
        IRoleRepository roles,
        IRefreshTokenRepository refreshTokens,
        IUnitOfWork uow,
        IClock clock,
        IOptions<LankaMartOptions> options,
        ILogger<UserAdminService> logger)
    {
        _users         = users;
        _roles         = roles;
        _refreshTokens = refreshTokens;
        _uow           = uow;
        _clock         = clock;
        _options       = options.Value;
        _logger        = logger;
    }

    public async Task<PagedResult<UserDto>> SearchAsync(
        string? search, int page, int pageSize, CancellationToken ct = default)
    {
        page     = page < 1 ? 1 : page;
        pageSize = pageSize < 1
            ? _options.DefaultPageSize
            : Math.Min(pageSize, _options.MaxPageSize);

        var (items, total) = await _users.SearchAsync(search, page, pageSize, ct);

        return new PagedResult<UserDto>(items.Select(ToDto).ToList(), page, pageSize, total);
    }

    public async Task<UserDto> GetByIdAsync(long id, CancellationToken ct = default)
    {
        var user = await _users.GetByIdWithRolesAsync(id, ct)
                   ?? throw new NotFoundException("User", id);
        return ToDto(user);
    }

    // ========================================================================
    //  GRANT A ROLE                                            (SLIDE 37)
    //  One INSERT into user_roles. Nothing on the users row changes.
    // ========================================================================
    public async Task<UserDto> AssignRoleAsync(
        long userId, string roleName, CancellationToken ct = default)
    {
        var user = await _users.GetForUpdateAsync(userId, ct)
                   ?? throw new NotFoundException("User", userId);

        var role = await _roles.GetByNameAsync(roleName, ct)
                   ?? throw new BusinessRuleException(
                          $"Role '{roleName}' does not exist. Valid roles: Admin, Staff, Customer.");

        // The composite primary key (user_id, role_id) would reject a duplicate
        // with error 23505 anyway; checking first turns that into a clear 409.
        if (await _roles.FindUserRoleAsync(userId, role.Id, ct) is not null)
            throw new ConflictException($"User {userId} already has the '{roleName}' role.");

        _roles.AddUserRole(new UserRole
        {
            UserId     = userId,
            RoleId     = role.Id,
            AssignedAt = _clock.UtcNow
        });

        await _uow.SaveChangesAsync(ct);

        _logger.LogInformation("Granted role {Role} to user {UserId}", roleName, userId);

        // SLIDE 37, bottom card - THE TIMING NUANCE STUDENTS ALWAYS MISS.
        //
        // Roles live in the JWT. This user's CURRENT access token was signed
        // before the grant and still says what it said, so the new role only
        // takes effect when they next refresh - up to AccessTokenMinutes away.
        //
        // For a GRANT, waiting is harmless. For a REVOKE it is a security hole,
        // which is why RemoveRoleAsync below also kills the refresh tokens.
        return await GetByIdAsync(userId, ct);
    }

    public async Task<UserDto> RemoveRoleAsync(
        long userId, string roleName, CancellationToken ct = default)
    {
        var role = await _roles.GetByNameAsync(roleName, ct)
                   ?? throw new BusinessRuleException($"Role '{roleName}' does not exist.");

        var assignment = await _roles.FindUserRoleAsync(userId, role.Id, ct)
                         ?? throw new NotFoundException($"Role assignment for user {userId}", roleName);

        _roles.RemoveUserRole(assignment);

        // Revoking a privilege must not wait for a token to expire. Killing the
        // refresh tokens forces a full re-login, and the new token is signed
        // without the removed role. The old access token still works until it
        // expires - the honest, documented cost of stateless auth (SLIDE 32).
        // Systems that cannot tolerate that window check the database on every
        // request and pay the latency instead.
        var revoked = await _refreshTokens.RevokeAllForUserAsync(
            userId, _clock.UtcNow, "role-changed", ct);

        await _uow.SaveChangesAsync(ct);

        _logger.LogWarning(
            "Removed role {Role} from user {UserId} and revoked {Count} refresh token(s)",
            roleName, userId, revoked);

        return await GetByIdAsync(userId, ct);
    }

    public async Task<IReadOnlyList<string>> GetRolesAsync(CancellationToken ct = default)
    {
        var roles = await _roles.GetAllAsync(ct);
        return roles.Select(r => r.Name).ToList();
    }

    private static UserDto ToDto(User u)
        => new(u.Id, u.Name, u.Email, u.IsActive, u.RoleNames().ToList(), u.CreatedAt);
}
