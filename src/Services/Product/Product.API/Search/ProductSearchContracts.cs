using EventBus.Contracts;
using Product.API.Dtos;

namespace Product.API.Search;

public interface IProductSearchService
{
    Task<ProductSearchResult> SearchAsync(
        SearchProductRequest request,
        CancellationToken cancellationToken = default);
}

public sealed record ProductSearchResult(
    ProductSearchPageResponse? Page,
    string? ErrorMessage)
{
    public bool IsSuccess => Page is not null;

    public static ProductSearchResult Success(ProductSearchPageResponse page)
        => new(page, null);

    public static ProductSearchResult LimitExceeded(int maxOffsetItems)
        => new(null, ProductQueryLimit.Create(maxOffsetItems).ErrorMessage);
}

public record ProductSearchPageResponse(
    IReadOnlyList<ProductResponse> Items,
    long TotalItems,
    int CurrentPage,
    int PageSize);
