using LankaMart.Api.Common;
using LankaMart.Api.Dtos;
using LankaMart.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LankaMart.Api.Controllers;

// ============================================================================
//  SLIDE 38, IMPLEMENTED LINE FOR LINE.
//
//  This is the controller on that slide. Read the attributes down the file:
//
//     [AllowAnonymous]                  browsing the catalogue is public
//     [Authorize(Roles = "Staff,Admin") managing the catalogue is not
//     [Authorize(Roles = "Admin")]      deleting is narrower still
//
//  Three points worth making out loud:
//
//  1. This is DECLARATIVE security. The check is visible at the top of the
//     action, in a code review, in one screenshot. Compare with if-statements
//     buried inside action bodies, which reviewers skim past.
//
//  2. Steps 1-3 of the pipeline run BEFORE this code. An unauthenticated or
//     under-privileged request never reaches the method body at all.
//
//  3. The role NAMES here are the same strings that the token service put into
//     the JWT at login. That thread - roles table -> user_roles -> JWT claim ->
//     this attribute - is the entire RBAC story in one line of the request.
// ============================================================================
[ApiController]
[Route("api/v1/products")]
[Produces("application/json")]
public class ProductsController : ControllerBase
{
    private readonly IProductService _service;

    public ProductsController(IProductService service) => _service = service;

    /// <summary>Browse the catalogue. Public.</summary>
    [HttpGet]
    [AllowAnonymous]
    [ProducesResponseType(typeof(PagedResult<ProductDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<ProductDto>>> Search(
        [FromQuery] long? categoryId,
        [FromQuery] string? search,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
        => Ok(await _service.SearchAsync(categoryId, search, page, pageSize, ct));

    /// <summary>One product by id. Public.</summary>
    [HttpGet("{id:long}", Name = nameof(GetProductById))]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ProductDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ProductDto>> GetProductById(long id, CancellationToken ct)
    {
        var product = await _service.GetByIdAsync(id, ct);
        return product is null ? NotFound() : Ok(product);
    }

    /// <summary>Add a product. Staff or Admin.</summary>
    /// <remarks>
    /// Call this with no token (401), then with a Customer token (403). Those
    /// two responses are the whole of slide 28 in ten seconds.
    /// </remarks>
    [HttpPost]
    [Authorize(Roles = Roles.StaffOrAdmin)]
    [ProducesResponseType(typeof(ProductDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ProductDto>> Create(
        [FromBody] CreateProductDto dto, CancellationToken ct)
    {
        var created = await _service.CreateAsync(dto, ct);
        return CreatedAtAction(nameof(GetProductById), new { id = created.Id }, created);
    }

    /// <summary>Replace a product entirely. Staff or Admin.</summary>
    [HttpPut("{id:long}")]
    [Authorize(Roles = Roles.StaffOrAdmin)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Replace(
        long id, [FromBody] ReplaceProductDto dto, CancellationToken ct)
    {
        await _service.ReplaceAsync(id, dto, ct);
        return NoContent();
    }

    /// <summary>Update only the fields you send. Staff or Admin.</summary>
    [HttpPatch("{id:long}")]
    [Authorize(Roles = Roles.StaffOrAdmin)]
    [ProducesResponseType(typeof(ProductDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ProductDto>> Patch(
        long id, [FromBody] PatchProductDto dto, CancellationToken ct)
        => Ok(await _service.PatchAsync(id, dto, ct));

    /// <summary>Delete a product. Admin only.</summary>
    /// <remarks>
    /// Deleting a product that appears on an order returns 409: order_items
    /// references products with ON DELETE RESTRICT, so PostgreSQL refuses to
    /// let the application shred an invoice (SLIDE 14).
    /// </remarks>
    [HttpDelete("{id:long}")]
    [Authorize(Roles = Roles.Admin)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Delete(long id, CancellationToken ct)
    {
        await _service.DeleteAsync(id, ct);
        return NoContent();
    }
}
