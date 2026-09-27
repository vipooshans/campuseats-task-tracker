using LankaMart.Api.Common;
using LankaMart.Api.Dtos;

namespace LankaMart.Api.Services;

/// SLIDE 37 - the admin side of RBAC: listing users and moving them between
/// roles by inserting and deleting rows in the user_roles junction table.
public interface IUserAdminService
{
    Task<PagedResult<UserDto>> SearchAsync(
        string? search, int page, int pageSize, CancellationToken ct = default);

    Task<UserDto> GetByIdAsync(long id, CancellationToken ct = default);
    Task<UserDto> AssignRoleAsync(long userId, string roleName, CancellationToken ct = default);
    Task<UserDto> RemoveRoleAsync(long userId, string roleName, CancellationToken ct = default);
    Task<IReadOnlyList<string>> GetRolesAsync(CancellationToken ct = default);
}
