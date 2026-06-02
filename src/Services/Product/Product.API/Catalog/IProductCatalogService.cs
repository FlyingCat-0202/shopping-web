using Product.API.Dtos;

namespace Product.API.Catalog;

internal interface IProductCatalogService
{
    Task<ProductCatalogResult> GetCatalogAsync(
        ProductCatalogRequest request,
        CancellationToken cancellationToken);
}
