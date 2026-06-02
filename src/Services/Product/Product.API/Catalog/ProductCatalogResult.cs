using Product.API.Dtos;

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
            $"Catalog chỉ hỗ trợ phân trang offset trong {maxOffsetItems:N0} sản phẩm đầu. Hãy dùng search/filter để thu hẹp kết quả.");
}
