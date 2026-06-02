using EventBus.Contracts;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Product.API.Dtos;
using Product.Domain.Entities;
using Product.Infrastructure.Data;

namespace Product.API.Products;

internal sealed class ProductAdminCommandService(
    ProductDbContext db,
    IPublishEndpoint publishEndpoint)
    : IProductAdminCommandService
{
    public async Task<ProductOperationResult<CategoryResponse>> CreateCategoryAsync(
        CategoryRequest request,
        CancellationToken cancellationToken)
    {
        var categoryName = request.Name.Trim();
        var normalizedName = categoryName.ToLowerInvariant();

        var categoryExists = await db.Categories
            .AsNoTracking()
            .AnyAsync(c => c.Name.ToLower() == normalizedName, cancellationToken);

        if (categoryExists)
            return ProductOperationResult<CategoryResponse>.Conflict("Danh mục đã tồn tại.");

        var category = new Category
        {
            Name = categoryName,
            Description = NormalizeOptionalText(request.Description)
        };

        db.Categories.Add(category);
        await db.SaveChangesAsync(cancellationToken);

        return ProductOperationResult<CategoryResponse>.Created(
            new CategoryResponse(category.Id, category.Name, category.Description));
    }

    public async Task<ProductOperationResult<ProductCommandAccepted>> QueueCreateProductAsync(
        ProductRequest request,
        CancellationToken cancellationToken)
    {
        var categoryExists = await db.Categories
            .AsNoTracking()
            .AnyAsync(c => c.Id == request.CategoryId, cancellationToken);

        if (!categoryExists)
            return ProductOperationResult<ProductCommandAccepted>.BadRequest("Danh mục không hợp lệ.");

        await publishEndpoint.Publish(
            new CreateProductRequest(
                request.Name,
                request.Price,
                request.StockQuantity,
                request.CategoryId,
                request.Description,
                request.ImageUrl,
                request.IsActive),
            cancellationToken);

        await db.SaveChangesAsync(cancellationToken);

        return ProductOperationResult<ProductCommandAccepted>.Accepted(
            new ProductCommandAccepted(),
            "Yêu cầu tạo sản phẩm đã được đưa vào hàng đợi.");
    }

    public async Task<ProductOperationResult<ProductCommandAccepted>> QueueUpdateProductAsync(
        Guid productId,
        UpdateProductRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Id != productId)
            return ProductOperationResult<ProductCommandAccepted>.BadRequest("Product id trong route và body không khớp.");

        var productExists = await db.Products
            .AsNoTracking()
            .AnyAsync(p => p.Id == productId && p.IsActive, cancellationToken);

        if (!productExists)
            return ProductOperationResult<ProductCommandAccepted>.NotFound("Sản phẩm không tồn tại hoặc đã ngừng kinh doanh.");

        var categoryExists = await db.Categories
            .AsNoTracking()
            .AnyAsync(c => c.Id == request.CategoryId, cancellationToken);

        if (!categoryExists)
            return ProductOperationResult<ProductCommandAccepted>.NotFound("Danh mục không tồn tại.");

        await publishEndpoint.Publish(
            new UpdateProductRequest(
                productId,
                request.Name,
                request.Price,
                request.StockQuantity,
                request.IsActive,
                request.Description,
                request.ImgUrl,
                request.CategoryId),
            cancellationToken);

        await db.SaveChangesAsync(cancellationToken);

        return ProductOperationResult<ProductCommandAccepted>.Accepted(
            new ProductCommandAccepted(productId),
            "Yêu cầu cập nhật sản phẩm đã được đưa vào hàng đợi.");
    }

    public async Task<ProductOperationResult<ProductCommandAccepted>> QueueDeleteProductAsync(
        Guid productId,
        CancellationToken cancellationToken)
    {
        var productExists = await db.Products
            .AsNoTracking()
            .AnyAsync(p => p.Id == productId && p.IsActive, cancellationToken);

        if (!productExists)
            return ProductOperationResult<ProductCommandAccepted>.NotFound("Sản phẩm không tồn tại hoặc đã được xóa.");

        await publishEndpoint.Publish(new DeleteProductRequest(productId), cancellationToken);
        await db.SaveChangesAsync(cancellationToken);

        return ProductOperationResult<ProductCommandAccepted>.Accepted(
            new ProductCommandAccepted(productId),
            "Yêu cầu xóa/ẩn sản phẩm đã được đưa vào hàng đợi.");
    }

    private static string? NormalizeOptionalText(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
