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

        query = ProductQueryPolicy.ApplyFilters(query, search.CategoryId, search.Stock);
        query = ProductQueryPolicy.ApplyKeyword(query, search.Keyword);

        var totalItems = await query.CountAsync(cancellationToken);
        var items = await ProductQueryPolicy.ApplySort(query, search.Sort)
            .Skip((int)search.Page.Offset)
            .Take(search.Page.PageSize)
            .Select(ProductQueryPolicy.ProductResponseProjection)
            .ToListAsync(cancellationToken);

        return new ProductSearchPageResponse(items, totalItems, search.Page.Page, search.Page.PageSize);
    }
}
