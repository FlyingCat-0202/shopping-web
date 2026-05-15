using EventBus.Extensions;
using EventBus.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Product.API.Dtos;
using Product.Domain.Entities;
using ProductEntity = Product.Domain.Entities.Product;
using Product.Infrastructure.Data;

namespace Product.API.Endpoints;

public static class ProductEndpoints
{
    public static void MapProductEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/products")
            .WithTags("Products")
            .AddEndpointFilter<IdempotencyFilter>();

        // Lấy danh sách products và categories
        group.MapGet("/", async (ProductDbContext db, ILogger<Program> logger, CancellationToken cancellationToken) =>
        {
            try
            {
                // 1. Lấy danh sách Products
                var productList = await db.Products
                    .AsNoTracking()
                    .Where(p => p.IsActive)
                    .OrderBy(p => p.Name)
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

                // 2. Lấy danh sách Categories
                var categoryList = await db.Categories
                    .AsNoTracking()
                    .Select(c => new CategoryResponse(
                        c.Id,
                        c.Name,
                        c.Description
                    ))
                    .ToListAsync(cancellationToken);

                return Results.Ok(new ProductCategoryResponse(productList, categoryList));
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "(ProductService) Lỗi khi truy vấn dữ liệu tổng hợp");
                return Results.Problem("Lỗi không thể lấy dữ liệu");
            }
        });

        // Lấy danh sách categories
        group.MapGet("/categories", async (ProductDbContext db, CancellationToken cancellationToken) =>
        {
            var categories = await db.Categories
                .AsNoTracking()
                .OrderBy(c => c.Name)
                .Select(c => new CategoryResponse(c.Id, c.Name, c.Description))
                .ToListAsync(cancellationToken);

            return Results.Ok(categories);
        })
        .WithName("GetCategories");


        // Tìm Product theo id
        group.MapGet("/{id:guid}", async (Guid id, ProductDbContext db, ILogger<Program> logger, CancellationToken cancellationToken) =>
        {
            try
            {
                var product = await db.Products
                    .AsNoTracking()
                    .Where(p => p.Id == id && p.IsActive)
                    .Select(p => new ProductResponse(
                        p.Id,
                        p.Name,
                        p.Price,
                        p.StockQuantity,
                        p.Description,
                        p.ImageUrl,
                        p.CategoryId,
                        p.Category.Name))
                    .FirstOrDefaultAsync(cancellationToken);

                return product is not null ? Results.Ok(product) : Results.NotFound();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "(ProductService) DB lỗi không thể truy cập");
                return Results.Problem("Lỗi không thể lấy dữ liệu products");
            }
        });

        // Thay đổi category cho product
        group.MapPut("/{productId:guid}/category/{categoryId:int}", async (
            Guid productId,
            int categoryId,
            ProductDbContext db,
            CancellationToken cancellationToken) =>
        {
            var category = await db.Categories
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == categoryId, cancellationToken);

            if (category is null)
                return Results.NotFound(new { message = "Danh mục không tồn tại." });

            var product = await db.Products
                .FirstOrDefaultAsync(p => p.Id == productId && p.IsActive, cancellationToken);

            if (product is null)
                return Results.NotFound(new { message = "Sản phẩm không tồn tại hoặc đã ngừng kinh doanh." });

            product.CategoryId = category.Id;
            await db.SaveChangesAsync(cancellationToken);

            return Results.Ok(new
            {
                message = "Đã cập nhật danh mục sản phẩm.",
                ProductId = product.Id,
                CategoryId = category.Id
            });
        })
        .RequireAuthorization(EndpointHelpers.AdminOnly);


        // Xóa Product theo id
        group.MapDelete("/{id:guid}", async (
            Guid id,
            ProductDbContext db,
            CancellationToken cancellationToken) =>
        {
            var product = await db.Products
                .FirstOrDefaultAsync(p => p.Id == id && p.IsActive, cancellationToken);

            if (product is null)
                return Results.NotFound(new { message = "Sản phẩm không tồn tại hoặc đã được xóa." });

            product.IsActive = false;
            product.StockQuantity = 0;
            await db.SaveChangesAsync(cancellationToken);

            return Results.Ok(new { message = "Đã xóa/ẩn sản phẩm.", ProductId = id });
        })
        .RequireAuthorization(EndpointHelpers.AdminOnly);

        group.MapPost("/categories", async (
            CategoryRequest request,
            ProductDbContext db,
            CancellationToken cancellationToken) =>
        {
            var categoryName = request.Name.Trim();
            var normalizedName = categoryName.ToLowerInvariant();

            var categoryExists = await db.Categories
                .AsNoTracking()
                .AnyAsync(c => c.Name.ToLower() == normalizedName, cancellationToken);

            if (categoryExists)
                return Results.Conflict(new { message = "Danh mục đã tồn tại." });

            var category = new Category
            {
                Name = categoryName,
                Description = string.IsNullOrWhiteSpace(request.Description)
                    ? null
                    : request.Description.Trim()
            };

            db.Categories.Add(category);
            await db.SaveChangesAsync(cancellationToken);

            return Results.Created(
                $"/api/products/categories/{category.Id}",
                new CategoryResponse(category.Id, category.Name, category.Description));
        })
        .AddEndpointFilter<ValidationFilter<CategoryRequest>>()
        .RequireAuthorization(EndpointHelpers.AdminOnly)
        .WithName("CreateCategory");


        // Tạo một sản phẩm với id ngẫu nhiên
        group.MapPost("/", async (
            ProductRequest request,
            ProductDbContext db,
            CancellationToken cancellationToken) =>
        {
            var category = await db.Categories
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == request.CategoryId, cancellationToken);

            if (category is null)
                return Results.BadRequest(new { message = "Danh mục không hợp lệ." });

            var product = new ProductEntity
            {
                Id = Guid.NewGuid(),
                Name = request.Name.Trim(),
                Price = request.Price,
                StockQuantity = request.StockQuantity,
                Description = string.IsNullOrWhiteSpace(request.Description)
                    ? null
                    : request.Description.Trim(),
                ImageUrl = string.IsNullOrWhiteSpace(request.ImageUrl)
                    ? null
                    : request.ImageUrl.Trim(),
                CategoryId = category.Id,
                IsActive = request.IsActive
            };

            db.Products.Add(product);
            await db.SaveChangesAsync(cancellationToken);

            return Results.Created(
                $"/api/products/{product.Id}",
                new ProductResponse(
                    product.Id,
                    product.Name,
                    product.Price,
                    product.StockQuantity,
                    product.Description,
                    product.ImageUrl,
                    product.CategoryId,
                    category.Name));
        })
        .AddEndpointFilter<ValidationFilter<ProductRequest>>()
        .RequireAuthorization(EndpointHelpers.AdminOnly);
    }
}
