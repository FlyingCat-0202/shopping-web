using Microsoft.EntityFrameworkCore;
using Product.API.Dtos;
using Product.Infrastructure.Data;

namespace Product.API.Search;

internal interface IProductDatabaseSearch
{
    Task<ProductSearchPageResponse> SearchAsync(ProductSearchQuery query, CancellationToken cancellationToken);
}

internal sealed class ProductDatabaseSearch(ProductDbContext db) : IProductDatabaseSearch
{
    public async Task<ProductSearchPageResponse> SearchAsync(
        ProductSearchQuery search,
        CancellationToken cancellationToken)
    {
        var query = db.Products
            .AsNoTracking()
            .Where(p => p.IsActive);

        query = ProductSearchFilters.ApplyProductFilters(query, search.CategoryId, search.Stock);

        if (!string.IsNullOrWhiteSpace(search.Keyword))
        {
            var pattern = $"%{search.Keyword}%";
            query = query.Where(p =>
                EF.Functions.ILike(p.Name, pattern) ||
                (p.Description != null && EF.Functions.ILike(p.Description, pattern)) ||
                EF.Functions.ILike(p.Category.Name, pattern));
        }

        var totalItems = await query.CountAsync(cancellationToken);
        var items = await ProductSearchFilters.ApplyProductSort(query, search.Sort)
            .Skip((search.Page - 1) * search.PageSize)
            .Take(search.PageSize)
            .Select(p => new ProductResponse(
                p.Id,
                p.Name,
                p.Price,
                p.StockQuantity,
                p.Description,
                p.ImageUrl,
                p.CategoryId,
                p.Category.Name))
            .ToListAsync(cancellationToken);

        return new ProductSearchPageResponse(items, totalItems, search.Page);
    }
}
