using Microsoft.EntityFrameworkCore;
using Product.API.Dtos;
using Product.API.Search;
using Product.Infrastructure.Data;

namespace Product.API.Products;

internal sealed class ProductReadService(ProductDbContext db) : IProductReadService
{
    public async Task<IReadOnlyList<CategoryResponse>> GetCategoriesAsync(CancellationToken cancellationToken)
        => await db.Categories
            .AsNoTracking()
            .OrderBy(c => c.Name)
            .Select(c => new CategoryResponse(c.Id, c.Name, c.Description))
            .ToListAsync(cancellationToken);

    public async Task<ProductResponse?> GetProductByIdAsync(Guid productId, CancellationToken cancellationToken)
        => await db.Products
            .AsNoTracking()
            .Where(p => p.Id == productId && p.IsActive)
            .Select(ProductQueryPolicy.ProductResponseProjection)
            .FirstOrDefaultAsync(cancellationToken);
}
