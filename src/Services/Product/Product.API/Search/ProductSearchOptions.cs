namespace Product.API.Search;

internal sealed class ProductSearchOptions
{
    public const string SectionName = "ProductSearch";

    public string Mode { get; init; } = ProductSearchMode.Hybrid;
    public int ElasticTimeoutSeconds { get; init; } = 4;
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
    int Page,
    int PageSize,
    int? CategoryId,
    string? CategoryName,
    string? Stock,
    string? StockStatus,
    string? Sort);
