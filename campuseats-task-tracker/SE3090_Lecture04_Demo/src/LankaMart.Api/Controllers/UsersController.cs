using LankaMart.Api.Common;
using LankaMart.Api.Dtos;
using LankaMart.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LankaMart.Api.Controllers;

// ============================================================================
//  SLIDE 36/37 - the administration surface of RBAC.
//
//  Note that granting a role is a POST to a sub-resource
//  (/users/42/roles) rather than a PUT to the user. The role assignment is a
//  row in its own table, and the URL says so.
// ============================================================================
[ApiController]
[Route("api/v1/users")]
[Produces("application/json")]
[Authorize(Roles = Roles.Admin)]        // the whole controller is Admin-only
public class UsersController : ControllerBase
{
    private readonly IUserAdminService _users;

    public UsersController(IUserAdminService users) => _users = users;

    /// <summary>List users with their roles. Admin only.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<UserDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<PagedResult<UserDto>>> Search(
        [FromQuery] string? search,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
        => Ok(await _users.SearchAsync(search, page, pageSize, ct));

    /// <summary>One user. Admin only.</summary>
    [HttpGet("{id:long}")]
    [ProducesResponseType(typeof(UserDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<UserDto>> GetById(long id, CancellationToken ct)
        => Ok(await _users.GetByIdAsync(id, ct));

    /// <summary>Grant a role. One INSERT into user_roles.</summary>
    /// <remarks>
    /// Try this live: promote the demo customer to Staff, then have them call
    /// POST /api/v1/products with their EXISTING token - still 403, because
    /// roles are baked into the token at login. Refresh, and it works. That is
    /// the stateless trade-off from slide 37, visible in thirty seconds.
    /// </remarks>
    [HttpPost("{id:long}/roles")]
    [ProducesResponseType(typeof(UserDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<UserDto>> AssignRole(
        long id, [FromBody] AssignRoleDto dto, CancellationToken ct)
        => Ok(await _users.AssignRoleAsync(id, dto.Role, ct));

    /// <summary>Revoke a role. Also revokes the user's refresh tokens.</summary>
    [HttpDelete("{id:long}/roles/{role}")]
    [ProducesResponseType(typeof(UserDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<UserDto>> RemoveRole(
        long id, string role, CancellationToken ct)
        => Ok(await _users.RemoveRoleAsync(id, role, ct));
}
