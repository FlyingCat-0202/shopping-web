using System.Net.Http.Headers;

namespace Cart.API.Clients;

public class ProductCatalogClient(HttpClient httpClient, IHttpContextAccessor httpContextAccessor) : IProductCatalogClient
{
    public async Task<List<SharedProductResponse>> GetProductsByIdsAsync(
        IEnumerable<Guid> productIds,
        CancellationToken cancellationToken = default)
    {
        var products = new List<SharedProductResponse>();
        var authorization = httpContextAccessor.HttpContext?.Request.Headers.Authorization.ToString();

        foreach (var productId in productIds.Distinct())
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/products/{productId}");
            if (!string.IsNullOrWhiteSpace(authorization))
                request.Headers.Authorization = AuthenticationHeaderValue.Parse(authorization);

            using var response = await httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
                continue;

            var product = await response.Content.ReadFromJsonAsync<SharedProductResponse>(cancellationToken: cancellationToken);
            if (product is not null)
                products.Add(product);
        }

        return products;
    }
}
