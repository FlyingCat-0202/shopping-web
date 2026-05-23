using Elastic.Clients.Elasticsearch;
using Elastic.Clients.Elasticsearch.QueryDsl;
using EventBus.Contracts;
using EventBus.Extensions;
using EventBus.Infrastructure;
using MassTransit;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Product.API.Dtos;
using Product.API.IntegrationEvents.Consumers.Elastic;
using Product.API.Seed;
using Product.Domain.Entities;
using Product.Infrastructure.Data;

namespace Product.API.Endpoints;

public static class ProductEndpoints
{
    //private const string ProductCreateRoutingKey = "product-create";
    //private const string ProductDeleteRoutingKey = "product-delete";
    //private const string ProductUpdateRoutingKey = "product-update";

    public static void MapProductEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/products")
            .WithTags("Products");

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

        // Gộp tất cả vào 1 Endpoint chuẩn RESTful: PUT /api/products/{productId}
        group.MapPut("/{productId:guid}", async (
            Guid productId,
            [FromBody] UpdateProductRequest request, // Lấy dữ liệu từ JSON Body
            ProductDbContext db,
            IPublishEndpoint publishEndpoint,
            CancellationToken cancellationToken) =>
        {
            // 1. Kiểm tra sản phẩm có tồn tại không
            var productExists = await db.Products
                .AsNoTracking()
                .AnyAsync(p => p.Id == productId && p.IsActive, cancellationToken);

            if (!productExists)
                return Results.NotFound(new { message = "Sản phẩm không tồn tại hoặc đã ngừng kinh doanh." });

            // 2. (Tùy chọn) Kiểm tra Category nếu categoryId bị đổi
            var categoryExists = await db.Categories
                .AsNoTracking()
                .AnyAsync(c => c.Id == request.CategoryId, cancellationToken);

            if (!categoryExists)
                return Results.NotFound(new { message = "Danh mục không tồn tại." });

            // 3. Đóng gói dữ liệu gửi lên Exchange
            var updateMsg = new UpdateProductRequest(
                productId,
                request.Name,
                request.Price,
                request.StockQuantity,
                request.IsActive,
                request.Description,
                request.ImgUrl,
                request.CategoryId
            );

            await SendMessage(updateMsg, publishEndpoint, cancellationToken);

            // 4. Lưu vào Outbox
            await db.SaveChangesAsync(cancellationToken);

            return Results.Accepted(null, new
            {
                message = "Yêu cầu cập nhật sản phẩm đã được đưa vào hàng đợi.",
                ProductId = productId
            });
        })
        .RequireAuthorization(EndpointHelpers.AdminOnly)
        .AddEndpointFilter<ValidationFilter<UpdateProductRequest>>() // Nếu bạn có fluent validation
        .AddEndpointFilter<IdempotencyFilter>();

        // Xóa Product theo id
        group.MapDelete("/{id:guid}", async (
            Guid id,
            ProductDbContext db,
            IPublishEndpoint publishEndpoint,
            CancellationToken cancellationToken) =>
        {
            var product = await db.Products
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.Id == id && p.IsActive, cancellationToken);

            if (product is null)
                return Results.NotFound(new { message = "Sản phẩm không tồn tại hoặc đã được xóa." });

            await SendMessage(
                new DeleteProductRequest(id),
                publishEndpoint,
                cancellationToken);
            await db.SaveChangesAsync(cancellationToken);

            return Results.Accepted(null, new { message = "Yêu cầu xóa/ẩn sản phẩm đã được đưa vào hàng đợi.", ProductId = id });
        })
        .RequireAuthorization(EndpointHelpers.AdminOnly)
        .AddEndpointFilter<IdempotencyFilter>();

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
        .WithName("CreateCategory")
        .AddEndpointFilter<IdempotencyFilter>();


        // Tạo một sản phẩm với id ngẫu nhiên
        group.MapPost("/", async (
            ProductRequest request,
            ProductDbContext db,
            IPublishEndpoint publishEndpoint,
            CancellationToken cancellationToken) =>
        {
            var category = await db.Categories
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == request.CategoryId, cancellationToken);

            if (category is null)
                return Results.BadRequest(new { message = "Danh mục không hợp lệ." });

            var msg = new CreateProductRequest(
                request.Name,
                request.Price,
                request.StockQuantity,
                category.Id,
                request.Description,
                request.ImageUrl,
                request.IsActive);

            await SendMessage(msg, publishEndpoint, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);

            return Results.Accepted(null, new { message = "Yêu cầu tạo sản phẩm đã được đưa vào hàng đợi." });
        })
        .AddEndpointFilter<ValidationFilter<ProductRequest>>()
        .RequireAuthorization(EndpointHelpers.AdminOnly)
        .AddEndpointFilter<IdempotencyFilter>();

        group.MapGet("/search", async (
            [AsParameters] SearchProductRequest request,
            ProductDbContext db,
            ElasticsearchClient elasticClient,
            ILogger<Program> logger,
            CancellationToken cancellationToken) =>
        {
            var page = Math.Max(request.Page, 1);
            var pageSize = Math.Clamp(request.PageSize, 1, 50);
            var keyword = request.Keyword?.Trim() ?? string.Empty;
            var from = (page - 1) * pageSize;

            try
            {
                var searchResponse = await elasticClient.SearchAsync<ProductEsDocument>(s => s
                    .Indices("products")
                    .From(from)
                    .Size(pageSize)
                    .Query(q => q
                        .Bool(b => b
                            .Filter(f => f
                                .Term(t => t.Field(p => p.IsActive).Value(true))
                            )
                            .Must(m =>
                            {
                                if (string.IsNullOrWhiteSpace(keyword))
                                {
                                    m.MatchAll();
                                }
                                else
                                {
                                    m.MultiMatch(mm => mm
                                        .Query(keyword)
                                        .Fields(new[] { "name", "categoryName", "description" })
                                        .Fuzziness(new Fuzziness("AUTO"))
                                    );
                                }
                            })
                        )
                    )
                , cancellationToken);

                if (searchResponse.IsValidResponse)
                {
                    return Results.Ok(new
                    {
                        TotalItems = searchResponse.Total,
                        CurrentPage = page,
                        Items = searchResponse.Documents
                    });
                }

                logger.LogWarning(
                    "Elasticsearch search failed. Falling back to database search. Details: {DebugInformation}",
                    searchResponse.DebugInformation);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Elasticsearch search threw an exception. Falling back to database search.");
            }

            return await SearchProductsFromDatabase(db, keyword, page, pageSize, cancellationToken);
        });

        // API Seed Dữ liệu mẫu
        group.MapPost("/seed", async (
            ProductDbContext db,
            IPublishEndpoint publishEndpoint,
            ILogger<Program> logger) =>
        {
            try
            {
                await ProductSeedData.SeedAsync(db, publishEndpoint, logger);
                return Results.Ok(new { message = "Đã chạy lệnh Seed dữ liệu thành công! Hãy kiểm tra Elasticsearch." });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Lỗi trong quá trình Seed data");
                return Results.Problem("Có lỗi xảy ra khi Seed dữ liệu: " + ex.Message);
            }
        })
        .WithName("SeedProducts")
        .WithDescription("Tự động nạp 100 sản phẩm mẫu vào Database và Elasticsearch");
        // .RequireAuthorization(EndpointHelpers.AdminOnly); // Mở comment dòng này nếu bắt buộc phải có token Admin mới được seed
    }



    private static async Task SendMessage<T>(
        T msg,
        IPublishEndpoint publishEndpoint,
        CancellationToken cancellationToken)
    {
        await publishEndpoint.Publish(msg!, cancellationToken);
    }

    private static async Task<IResult> SearchProductsFromDatabase(
        ProductDbContext db,
        string keyword,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var query = db.Products
            .AsNoTracking()
            .Where(p => p.IsActive);

        if (!string.IsNullOrWhiteSpace(keyword))
        {
            var pattern = $"%{keyword}%";
            query = query.Where(p =>
                EF.Functions.ILike(p.Name, pattern) ||
                (p.Description != null && EF.Functions.ILike(p.Description, pattern)) ||
                EF.Functions.ILike(p.Category.Name, pattern));
        }

        var totalItems = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderBy(p => p.Name)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
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

        return Results.Ok(new
        {
            TotalItems = totalItems,
            CurrentPage = page,
            Items = items
        });
    }
}
