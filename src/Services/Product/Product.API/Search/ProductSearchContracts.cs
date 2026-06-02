using EventBus.Contracts;
using Product.API.Dtos;

namespace Product.API.Search;

public interface IProductSearchService
{
    Task<ProductSearchPageResponse> SearchAsync(
        SearchProductRequest request,
        CancellationToken cancellationToken = default);
}

public record ProductSearchPageResponse(
    IReadOnlyList<ProductResponse> Items,
    long TotalItems,
    int CurrentPage);
