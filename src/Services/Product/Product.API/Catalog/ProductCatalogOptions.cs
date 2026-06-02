namespace Product.API.Catalog;

internal sealed class ProductCatalogOptions
{
    public const string SectionName = "ProductCatalog";

    public int MaxPageSize { get; init; } = 50;
    public int MaxOffsetItems { get; init; } = 10_000;

    public int EffectiveMaxPageSize => Math.Clamp(MaxPageSize, 1, 100);
    public int EffectiveMaxOffsetItems => Math.Max(MaxOffsetItems, EffectiveMaxPageSize);
}
