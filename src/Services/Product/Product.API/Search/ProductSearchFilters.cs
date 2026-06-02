using Product.API.IntegrationEvents.Consumers.Elastic;
using ProductEntity = Product.Domain.Entities.Product;

namespace Product.API.Search;

internal static class ProductSearchFilters
{
    internal static IQueryable<ProductEntity> ApplyProductFilters(
        IQueryable<ProductEntity> query,
        int? categoryId,
        string? stock)
    {
        if (categoryId is > 0)
            query = query.Where(p => p.CategoryId == categoryId.Value);

        return stock?.Trim().ToLowerInvariant() switch
        {
            "instock" => query.Where(p => p.StockQuantity > 0),
            "outofstock" => query.Where(p => p.StockQuantity <= 0),
            _ => query
        };
    }

    internal static IOrderedQueryable<ProductEntity> ApplyProductSort(
        IQueryable<ProductEntity> query,
        string? sort)
        => sort?.Trim().ToLowerInvariant() switch
        {
            "price-asc" => query.OrderBy(p => p.Price).ThenBy(p => p.Id),
            "price-desc" => query.OrderByDescending(p => p.Price).ThenBy(p => p.Id),
            "name" => query.OrderBy(p => p.Name).ThenBy(p => p.Id),
            _ => query.OrderBy(p => p.Name).ThenBy(p => p.Id)
        };

    internal static string? NormalizeElasticStockStatus(string? stock)
        => stock?.Trim().ToLowerInvariant() switch
        {
            "instock" => ElasticProductIndex.InStock,
            "outofstock" => ElasticProductIndex.OutOfStock,
            _ => null
        };
}
