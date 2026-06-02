using EventBus.Contracts;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Product.Infrastructure.Data;
using ProductEntity = Product.Domain.Entities.Product;

namespace Product.API.Products;

internal sealed class ProductMutationService(
    ProductDbContext db,
    IPublishEndpoint publishEndpoint)
    : IProductMutationService
{
    public async Task CreateProductAsync(CreateProductRequest request, CancellationToken cancellationToken)
    {
        var category = await db.Categories
            .AsNoTracking()
            .Where(c => c.Id == request.CategoryId)
            .Select(c => new { c.Id, c.Name })
            .FirstOrDefaultAsync(cancellationToken);

        if (category is null)
            throw new InvalidOperationException($"Category {request.CategoryId} không tồn tại.");

        var product = new ProductEntity
        {
            Id = Guid.NewGuid(),
            Name = request.Name.Trim(),
            Price = request.Price,
            StockQuantity = request.StockQuantity,
            Description = NormalizeOptionalText(request.Description),
            ImageUrl = NormalizeOptionalText(request.ImageUrl),
            CategoryId = category.Id,
            IsActive = request.IsActive,
        };

        db.Products.Add(product);

        await publishEndpoint.Publish(new ProductCreatedEvent(
            product.Id,
            product.Name,
            product.Description ?? string.Empty,
            product.Price,
            category.Name,
            product.IsActive,
            product.ImageUrl,
            product.CategoryId,
            product.StockQuantity), cancellationToken);

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateProductAsync(UpdateProductRequest request, CancellationToken cancellationToken)
    {
        var product = await db.Products
            .FirstOrDefaultAsync(p => p.Id == request.Id, cancellationToken);

        if (product is null)
            return;

        var categoryName = await db.Categories
            .Where(c => c.Id == request.CategoryId)
            .Select(c => c.Name)
            .FirstOrDefaultAsync(cancellationToken);

        if (categoryName is null)
            throw new InvalidOperationException($"Category {request.CategoryId} không tồn tại.");

        product.Name = request.Name.Trim();
        product.Price = request.Price;
        product.StockQuantity = request.StockQuantity;
        product.IsActive = request.IsActive;
        product.Description = NormalizeOptionalText(request.Description);
        product.ImageUrl = NormalizeOptionalText(request.ImgUrl);
        product.CategoryId = request.CategoryId;

        await publishEndpoint.Publish(new ProductUpdatedEvent(
            Id: product.Id,
            Name: product.Name,
            Price: product.Price,
            IsActive: product.IsActive,
            CategoryName: categoryName,
            Description: product.Description,
            ImageUrl: product.ImageUrl,
            CategoryId: product.CategoryId,
            StockQuantity: product.StockQuantity), cancellationToken);

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteProductAsync(DeleteProductRequest request, CancellationToken cancellationToken)
    {
        var product = await db.Products
            .FirstOrDefaultAsync(p => p.Id == request.Id, cancellationToken);

        if (product is null)
            return;

        if (!product.IsActive)
            return;

        product.IsActive = false;

        await publishEndpoint.Publish(new ProductDeletedEvent(product.Id), cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
    }

    private static string? NormalizeOptionalText(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
