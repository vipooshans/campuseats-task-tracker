using LankaMart.Api.Common;
using LankaMart.Api.Dtos;
using LankaMart.Api.Models;
using LankaMart.Api.Repositories;
using Microsoft.Extensions.Options;

namespace LankaMart.Api.Services;

// ============================================================================
//  Carried over from Lecture 03 almost unchanged - which is the point.
//
//  The repository underneath is now EF Core against PostgreSQL instead of a
//  ConcurrentDictionary, and this file barely noticed. No SQL appears here, no
//  DbContext, no connection string. That is what "depend on abstractions"
//  bought us (SLIDE 24).
//
//  What DID change: the category is now a foreign key, so create and update
//  must check the referenced row exists.
// ============================================================================
public sealed class ProductService : IProductService
{
    private readonly IProductRepository      _products;
    private readonly ICategoryRepository     _categories;
    private readonly IUnitOfWork             _uow;
    private readonly LankaMartOptions        _options;
    private readonly ILogger<ProductService> _logger;

    public ProductService(
        IProductRepository products,
        ICategoryRepository categories,
        IUnitOfWork uow,
        IOptions<LankaMartOptions> options,
        ILogger<ProductService> logger)
    {
        _products   = products;
        _categories = categories;
        _uow        = uow;
        _options    = options.Value;
        _logger     = logger;
    }

    public async Task<PagedResult<ProductDto>> SearchAsync(
        long? categoryId, string? search, int page, int pageSize, CancellationToken ct = default)
    {
        // Clamp the paging inputs: a client asking for pageSize=1000000 gets
        // MaxPageSize, not a melted server - and now, not a million-row SELECT.
        page     = page < 1 ? 1 : page;
        pageSize = pageSize < 1
            ? _options.DefaultPageSize
            : Math.Min(pageSize, _options.MaxPageSize);

        var (items, total) = await _products.SearchAsync(categoryId, search, page, pageSize, ct);

        return new PagedResult<ProductDto>(items.Select(ToDto).ToList(), page, pageSize, total);
    }

    public async Task<ProductDto?> GetByIdAsync(long id, CancellationToken ct = default)
    {
        var product = await _products.GetByIdAsync(id, ct);
        return product is null ? null : ToDto(product);
    }

    public async Task<ProductDto> CreateAsync(CreateProductDto dto, CancellationToken ct = default)
    {
        await EnsureCategoryExistsAsync(dto.CategoryId, ct);

        if (await _products.ExistsWithNameAsync(dto.Name, null, ct))
            throw new ConflictException($"A product named '{dto.Name}' already exists.");

        var product = new Product
        {
            Name             = dto.Name.Trim(),
            CategoryId       = dto.CategoryId,
            Price            = dto.Price,
            StockQty         = dto.StockQty,
            SupplierCostNote = "(internal - set by the purchasing team)"
        };

        await _products.AddAsync(product, ct);
        await _uow.SaveChangesAsync(ct);

        _logger.LogInformation("Created product {ProductId} named {ProductName}",
            product.Id, product.Name);

        // Re-read so the response carries the resolved category name.
        var saved = await _products.GetByIdAsync(product.Id, ct);
        return ToDto(saved!);
    }

    public async Task ReplaceAsync(long id, ReplaceProductDto dto, CancellationToken ct = default)
    {
        var existing = await _products.GetForUpdateAsync(id, ct)
                       ?? throw new NotFoundException("Product", id);

        await EnsureCategoryExistsAsync(dto.CategoryId, ct);

        if (await _products.ExistsWithNameAsync(dto.Name, id, ct))
            throw new ConflictException($"Another product named '{dto.Name}' already exists.");

        existing.Name       = dto.Name.Trim();
        existing.CategoryId = dto.CategoryId;
        existing.Price      = dto.Price;
        existing.StockQty   = dto.StockQty;

        // SLIDE 25 - change tracking. We never wrote an UPDATE statement;
        // EF Core compares the entity with its snapshot and writes one for us.
        await _uow.SaveChangesAsync(ct);

        _logger.LogInformation("Replaced product {ProductId}", id);
    }

    public async Task<ProductDto> PatchAsync(
        long id, PatchProductDto dto, CancellationToken ct = default)
    {
        var existing = await _products.GetForUpdateAsync(id, ct)
                       ?? throw new NotFoundException("Product", id);

        if (dto.CategoryId is not null)
            await EnsureCategoryExistsAsync(dto.CategoryId.Value, ct);

        if (dto.Name is not null && await _products.ExistsWithNameAsync(dto.Name, id, ct))
            throw new ConflictException($"Another product named '{dto.Name}' already exists.");

        existing.Name       = dto.Name?.Trim()  ?? existing.Name;
        existing.CategoryId = dto.CategoryId    ?? existing.CategoryId;
        existing.Price      = dto.Price         ?? existing.Price;
        existing.StockQty   = dto.StockQty      ?? existing.StockQty;

        await _uow.SaveChangesAsync(ct);

        var saved = await _products.GetByIdAsync(id, ct);
        return ToDto(saved!);
    }

    public async Task DeleteAsync(long id, CancellationToken ct = default)
    {
        var existing = await _products.GetForUpdateAsync(id, ct)
                       ?? throw new NotFoundException("Product", id);

        _products.Remove(existing);

        // If this product appears on any order, order_items -> products is
        // ON DELETE RESTRICT and PostgreSQL will refuse: error 23503, which
        // GlobalExceptionHandler turns into 409 Conflict (SLIDE 26).
        // That is the database defending an invoice from the application.
        // Demo it: DELETE a product that has been ordered.
        await _uow.SaveChangesAsync(ct);

        _logger.LogInformation("Deleted product {ProductId}", id);
    }

    public async Task<IReadOnlyList<CategoryDto>> GetCategoriesAsync(CancellationToken ct = default)
    {
        var categories = await _categories.GetAllAsync(ct);
        return categories.Select(c => new CategoryDto(c.Id, c.Name, c.Products.Count)).ToList();
    }

    /// Checking the foreign key here gives the client a clear 400 with the id
    /// that was wrong. Skipping it would still be SAFE - PostgreSQL rejects the
    /// row with error 23503 - but the message would be far less helpful.
    /// Application validation for UX, database constraint for truth (SLIDE 39).
    private async Task EnsureCategoryExistsAsync(long categoryId, CancellationToken ct)
    {
        if (await _categories.GetByIdAsync(categoryId, ct) is null)
            throw new BusinessRuleException(
                $"Category {categoryId} does not exist. Call GET /api/v1/categories for valid ids.");
    }

    private static ProductDto ToDto(Product p)
        => new(p.Id, p.Name, p.CategoryId, p.Category?.Name ?? "", p.Price, p.StockQty, p.StockQty > 0);
}
