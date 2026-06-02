namespace Product.API.Search;

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
