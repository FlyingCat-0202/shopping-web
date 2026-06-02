namespace Product.API.Search;

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
