using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Product.API.Dtos;
using Product.API.Search;
using Product.Infrastructure.Data;

namespace Product.API.Catalog;

internal sealed class ProductCatalogService(
    ProductDbContext db,
    IOptions<ProductCatalogOptions> options)
    : IProductCatalogService
{
    public async Task<ProductCatalogResult> GetCatalogAsync(
        ProductCatalogRequest request,
        CancellationToken cancellationToken)
    {
        var page = ProductQueryPage.Normalize(
            request.Page,
            request.PageSize,
            options.Value.EffectiveMaxPageSize);
        var limit = ProductQueryLimit.Create(options.Value.EffectiveMaxOffsetItems);

        if (!limit.Allows(page))
            return ProductCatalogResult.LimitExceeded(options.Value.EffectiveMaxOffsetItems);

        var query = db.Products
            .AsNoTracking()
            .Where(p => p.IsActive);

        var stock = ProductQueryPolicy.NormalizeStock(request.Stock);
        var sort = ProductQueryPolicy.NormalizeSort(request.Sort);
        query = ProductQueryPolicy.ApplyFilters(query, request.CategoryId, stock);

        var totalItems = await query.CountAsync(cancellationToken);
        var products = await ProductQueryPolicy.ApplySort(query, sort)
            .Skip((int)page.Offset)
            .Take(page.PageSize)
            .Select(ProductQueryPolicy.ProductResponseProjection)
            .ToListAsync(cancellationToken);

        var categories = request.IncludeCategories
            ? await db.Categories
                .AsNoTracking()
                .OrderBy(c => c.Name)
                .Select(c => new CategoryResponse(c.Id, c.Name, c.Description))
                .ToListAsync(cancellationToken)
            : [];

        return ProductCatalogResult.Success(new ProductCategoryPageResponse(
            products,
            categories,
            totalItems,
            page.Page,
            page.PageSize));
    }
}
