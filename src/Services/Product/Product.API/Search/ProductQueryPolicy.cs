using System.Linq.Expressions;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Product.API.Dtos;
using Product.API.Search.Indexing;
using ProductEntity = Product.Domain.Entities.Product;

namespace Product.API.Search;

internal static class ProductQueryPolicy
{
    internal const string SortFeatured = "featured";
    internal const string SortName = "name";
    internal const string SortPriceAsc = "price-asc";
    internal const string SortPriceDesc = "price-desc";

    internal const string StockAll = "all";
    internal const string StockInStock = "instock";
    internal const string StockOutOfStock = "outofstock";

    private static readonly char[] LikeWildcards = ['%', '_', '\\'];

    internal static IQueryable<ProductEntity> ApplyFilters(
        IQueryable<ProductEntity> query,
        int? categoryId,
        string stock)
    {
        if (categoryId is > 0)
            query = query.Where(p => p.CategoryId == categoryId.Value);

        return stock switch
        {
            StockInStock => query.Where(p => p.StockQuantity > 0),
            StockOutOfStock => query.Where(p => p.StockQuantity <= 0),
            _ => query
        };
    }

    internal static IQueryable<ProductEntity> ApplyKeyword(
        IQueryable<ProductEntity> query,
        string keyword)
    {
        if (string.IsNullOrWhiteSpace(keyword))
            return query;

        var pattern = $"%{EscapeLikePattern(keyword)}%";
        return query.Where(p =>
            EF.Functions.ILike(p.Name, pattern, "\\") ||
            (p.Description != null && EF.Functions.ILike(p.Description, pattern, "\\")) ||
            EF.Functions.ILike(p.Category.Name, pattern, "\\"));
    }

    internal static IOrderedQueryable<ProductEntity> ApplySort(
        IQueryable<ProductEntity> query,
        string sort)
        => sort switch
        {
            SortPriceAsc => query.OrderBy(p => p.Price).ThenBy(p => p.Id),
            SortPriceDesc => query.OrderByDescending(p => p.Price).ThenBy(p => p.Id),
            SortName => query.OrderBy(p => p.Name).ThenBy(p => p.Id),
            _ => query.OrderBy(p => p.Name).ThenBy(p => p.Id)
        };

    internal static string NormalizeSort(string? sort)
        => sort?.Trim().ToLowerInvariant() switch
        {
            SortPriceAsc => SortPriceAsc,
            SortPriceDesc => SortPriceDesc,
            SortName => SortName,
            _ => SortFeatured
        };

    internal static string NormalizeStock(string? stock)
        => stock?.Trim().ToLowerInvariant() switch
        {
            StockInStock => StockInStock,
            StockOutOfStock => StockOutOfStock,
            _ => StockAll
        };

    internal static string? ToElasticStockStatus(string stock)
        => stock switch
        {
            StockInStock => ElasticProductIndex.InStock,
            StockOutOfStock => ElasticProductIndex.OutOfStock,
            _ => null
        };

    internal static Expression<Func<ProductEntity, ProductResponse>> ProductResponseProjection
        => p => new ProductResponse(
            p.Id,
            p.Name,
            p.Price,
            p.StockQuantity,
            p.Description,
            p.ImageUrl,
            p.CategoryId,
            p.Category.Name);

    internal static string EscapeLikePattern(string value)
    {
        var result = new StringBuilder(value.Trim());
        for (var index = 0; index < result.Length; index++)
        {
            if (!LikeWildcards.Contains(result[index]))
                continue;

            result.Insert(index, '\\');
            index++;
        }

        return result.ToString();
    }
}
