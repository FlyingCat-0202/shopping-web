namespace Product.API.Search;

internal sealed class ProductSearchOptions
{
    public const string SectionName = "ProductSearch";

    public string Mode { get; init; } = ProductSearchMode.Hybrid;
    public int MaxPageSize { get; init; } = 50;
    public int MaxOffsetItems { get; init; } = 10_000;
    public int ElasticTimeoutSeconds { get; init; } = 4;
    public bool FallbackToDatabaseWhenElasticReturnsNoResults { get; init; } = false;
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
