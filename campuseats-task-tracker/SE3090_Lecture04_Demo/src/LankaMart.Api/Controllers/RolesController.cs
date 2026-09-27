using LankaMart.Api.Common;
using LankaMart.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LankaMart.Api.Controllers;

/// SLIDE 37 - the roles table, read-only over HTTP. Creating roles is a
/// deployment-time decision (a seed or a migration), not something an API
/// client should do at 2am.
[ApiController]
[Route("api/v1/roles")]
[Produces("application/json")]
[Authorize(Roles = Common.Roles.Admin)]
public class RolesController : ControllerBase
{
    private readonly IUserAdminService _users;

    public RolesController(IUserAdminService users) => _users = users;

    /// <summary>The role names this system recognises. Admin only.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<string>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<IReadOnlyList<string>>> GetAll(CancellationToken ct)
        => Ok(await _users.GetRolesAsync(ct));
}
