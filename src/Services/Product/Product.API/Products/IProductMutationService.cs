using EventBus.Contracts;

namespace Product.API.Products;

internal interface IProductMutationService
{
    Task CreateProductAsync(CreateProductRequest request, CancellationToken cancellationToken);

    Task UpdateProductAsync(UpdateProductRequest request, CancellationToken cancellationToken);

    Task DeleteProductAsync(DeleteProductRequest request, CancellationToken cancellationToken);
}
