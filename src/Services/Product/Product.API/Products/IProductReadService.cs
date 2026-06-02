using Product.API.Dtos;

namespace Product.API.Products;

internal interface IProductReadService
{
    Task<IReadOnlyList<CategoryResponse>> GetCategoriesAsync(CancellationToken cancellationToken);

    Task<ProductResponse?> GetProductByIdAsync(Guid productId, CancellationToken cancellationToken);
}
