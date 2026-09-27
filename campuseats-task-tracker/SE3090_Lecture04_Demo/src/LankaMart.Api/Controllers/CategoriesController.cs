using LankaMart.Api.Dtos;
using LankaMart.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LankaMart.Api.Controllers;

/// SLIDE 18 - categories exist as a table now, so they get their own resource.
/// Clients need it: a product create request references a category by id, and
/// ids should be discovered, not guessed.
[ApiController]
[Route("api/v1/categories")]
[Produces("application/json")]
public class CategoriesController : ControllerBase
{
    private readonly IProductService _service;

    public CategoriesController(IProductService service) => _service = service;

    /// <summary>All categories. Public - the storefront needs them.</summary>
    [HttpGet]
    [AllowAnonymous]
    [ProducesResponseType(typeof(IReadOnlyList<CategoryDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<CategoryDto>>> GetAll(CancellationToken ct)
        => Ok(await _service.GetCategoriesAsync(ct));
}
