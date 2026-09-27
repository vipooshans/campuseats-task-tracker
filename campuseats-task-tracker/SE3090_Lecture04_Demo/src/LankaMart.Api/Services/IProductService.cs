using LankaMart.Api.Common;
using LankaMart.Api.Dtos;

namespace LankaMart.Api.Services;

public interface IProductService
{
    Task<PagedResult<ProductDto>> SearchAsync(
        long? categoryId, string? search, int page, int pageSize, CancellationToken ct = default);

    Task<ProductDto?> GetByIdAsync(long id, CancellationToken ct = default);
    Task<ProductDto>  CreateAsync(CreateProductDto dto, CancellationToken ct = default);
    Task             ReplaceAsync(long id, ReplaceProductDto dto, CancellationToken ct = default);
    Task<ProductDto> PatchAsync(long id, PatchProductDto dto, CancellationToken ct = default);
    Task             DeleteAsync(long id, CancellationToken ct = default);
    Task<IReadOnlyList<CategoryDto>> GetCategoriesAsync(CancellationToken ct = default);
}
