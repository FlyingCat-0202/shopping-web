using Product.API.Dtos;
using Product.API.Search;

namespace Product.API.Catalog;

internal sealed record ProductCatalogResult(
    ProductCategoryPageResponse? Page,
    string? ErrorMessage)
{
    public bool IsSuccess => Page is not null;

    public static ProductCatalogResult Success(ProductCategoryPageResponse page)
        => new(page, null);

    public static ProductCatalogResult LimitExceeded(int maxOffsetItems)
        => new(
            null,
            ProductQueryLimit.Create(maxOffsetItems).ErrorMessage);
}
