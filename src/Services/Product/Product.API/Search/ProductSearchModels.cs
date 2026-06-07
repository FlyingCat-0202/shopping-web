using Product.API.Dtos;

namespace Product.API.Search;

public sealed record SearchProductRequest(
    string? Keyword = null,
    int Page = 1,
    int PageSize = 10,
    int? CategoryId = null,
    string? Stock = null,
    string? Sort = null);

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

public sealed record ProductSearchPageResponse(
    IReadOnlyList<ProductResponse> Items,
    long TotalItems,
    int CurrentPage,
    int PageSize);

internal sealed class ProductSearchOptions
{
    public const string SectionName = "ProductSearch";

    public string Mode { get; init; } = ProductSearchMode.Hybrid;
    public int MaxPageSize { get; init; } = 50;
    public int MaxOffsetItems { get; init; } = 10_000;
    public int ElasticTimeoutSeconds { get; init; } = 4;
    public bool FallbackToDatabaseWhenElasticReturnsNoResults { get; init; }
    public bool ConfigureElasticIndexOnStartup { get; init; } = true;
    public bool RebuildElasticIndexWhenCreated { get; init; } = true;

    public bool UseElastic
        => !string.Equals(Mode, ProductSearchMode.Database, StringComparison.OrdinalIgnoreCase);

    public bool RequireElastic
        => string.Equals(Mode, ProductSearchMode.Elastic, StringComparison.OrdinalIgnoreCase);
}

internal static class ProductSearchMode
{
    public const string Database = "Database";
    public const string Elastic = "Elastic";
    public const string Hybrid = "Hybrid";
}

internal sealed record ProductSearchQuery(
    string Keyword,
    ProductQueryPage Page,
    int? CategoryId,
    string Stock,
    string? StockStatus,
    string Sort);

internal sealed record ProductQueryPage(
    int Page,
    int PageSize,
    long Offset)
{
    public static ProductQueryPage Normalize(
        int page,
        int pageSize,
        int maxPageSize)
    {
        var normalizedPage = Math.Max(page, 1);
        var normalizedPageSize = Math.Clamp(pageSize, 1, Math.Clamp(maxPageSize, 1, 100));
        var offset = ((long)normalizedPage - 1) * normalizedPageSize;

        return new ProductQueryPage(normalizedPage, normalizedPageSize, offset);
    }
}

internal sealed record ProductQueryLimit(
    int MaxOffsetItems,
    string ErrorMessage)
{
    public bool Allows(ProductQueryPage page)
        => page.Offset + page.PageSize <= MaxOffsetItems;

    public static ProductQueryLimit Create(int maxOffsetItems)
    {
        var normalized = Math.Max(maxOffsetItems, 1);
        return new ProductQueryLimit(
            normalized,
            $"Product query chỉ hỗ trợ phân trang offset trong {normalized:N0} sản phẩm đầu. Hãy dùng search/filter để thu hẹp kết quả.");
    }
}
