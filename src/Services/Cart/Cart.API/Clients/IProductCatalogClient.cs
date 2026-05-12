namespace Cart.API.Clients;

public interface IProductCatalogClient
{
    Task<List<SharedProductResponse>> GetProductsByIdsAsync(IEnumerable<Guid> productIds, CancellationToken cancellationToken = default);
}
