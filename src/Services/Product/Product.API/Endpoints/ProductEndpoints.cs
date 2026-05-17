using EventBus.Contracts;
using EventBus.Extensions;
using EventBus.Infrastructure;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Product.API.Dtos;
using Product.Infrastructure.Data;

namespace Product.API.Endpoints;

public static class ProductEndpoints
{
    private const string ProductCreateRoutingKey = "product-create";
    private const string ProductDeleteRoutingKey = "product-delete";
    private const string ProductUpdateRoutingKey = "product-update";

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
                    .Select(p => new ProductResponse(
                        p.Id,
                        p.Name,
                        p.Price,
                        p.StockQuantity,
                        p.Description
                    ))
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


        // Tìm Product theo id
        group.MapGet("/{id:guid}", async (Guid id, ProductDbContext db, ILogger<Program> logger, CancellationToken cancellationToken) =>
        {
            try
            {
                var product = await db.Products
                    .AsNoTracking()
                    .Include(p => p.Category)
                    .Where(p => p.Id == id && p.IsActive)
                    .Select(p => new ProductResponse(p.Id, p.Name, p.Price, p.StockQuantity, p.Category != null ? p.Category.Name : "N/A"))
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
        group.MapPut("/{productId:guid}/category/{categoryId:int}", async (Guid productId, int categoryId, IBus rbmq, CancellationToken cancellationToken) =>
        {
            var updateMsg = new UpdateProductRequest(
                productId,
                categoryId
            );

            await SendMessage(updateMsg, rbmq, ProductUpdateRoutingKey, cancellationToken);

            return Results.Accepted(null, new { message = $"Yêu cầu thay đổi danh mục cho sản phẩm {productId} đang được xử lý." });
        })
        .RequireAuthorization(EndpointHelpers.AdminOnly)
        .AddEndpointFilter<IdempotencyFilter>();


        // Xóa Product theo id
        group.MapDelete("/{id:guid}", async (Guid id, IBus rbmq, CancellationToken cancellationToken) =>
        {
            var deleteProduct = new DeleteProductRequest(id);

            await SendMessage(deleteProduct, rbmq, ProductDeleteRoutingKey, cancellationToken);

            return Results.Accepted(null, new { message = "Yêu cầu xóa/ẩn sản phẩm đã được đưa vào hàng đợi." });
        })
        .RequireAuthorization(EndpointHelpers.AdminOnly)
        .AddEndpointFilter<IdempotencyFilter>();


        // Tạo một sản phẩm với id ngẫu nhiên
        group.MapPost("/", async (ProductRequest request, IBus rabbitFactory, CancellationToken cancellationToken) =>
        {
            var msg = new CreateProductRequest(
                Name: request.Name,
                Price: request.Price,
                StockQuantity: request.StockQuantity,
                CategoryId: request.CategoryId
             );

            await SendMessage(msg, rabbitFactory, ProductCreateRoutingKey, cancellationToken);

            return Results.Accepted(null, new { message = "Yêu cầu tạo sản phẩm đã được đưa vào hàng đợi." });
        })
        .AddEndpointFilter<ValidationFilter<ProductRequest>>()
        .RequireAuthorization(EndpointHelpers.AdminOnly)
        .AddEndpointFilter<IdempotencyFilter>();
    }

    // Gửi message đến broker
    private static async Task SendMessage<T>(T msg, IBus pe, string routingKey, CancellationToken cancellationToken)
    {
        if (msg is null)
        {
            return;
        }

        await pe.Publish(msg, context =>
        {
            context.SetRoutingKey(routingKey);
        }, cancellationToken);

    }
}
