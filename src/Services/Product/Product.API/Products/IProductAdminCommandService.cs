using EventBus.Contracts;
using Product.API.Dtos;

namespace Product.API.Products;

internal interface IProductAdminCommandService
{
    Task<ProductOperationResult<CategoryResponse>> CreateCategoryAsync(
        CategoryRequest request,
        CancellationToken cancellationToken);

    Task<ProductOperationResult<ProductCommandAccepted>> QueueCreateProductAsync(
        ProductRequest request,
        CancellationToken cancellationToken);

    Task<ProductOperationResult<ProductCommandAccepted>> QueueUpdateProductAsync(
        Guid productId,
        UpdateProductRequest request,
        CancellationToken cancellationToken);

    Task<ProductOperationResult<ProductCommandAccepted>> QueueDeleteProductAsync(
        Guid productId,
        CancellationToken cancellationToken);
}
