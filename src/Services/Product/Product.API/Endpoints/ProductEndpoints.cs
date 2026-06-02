using EventBus.Contracts;
using EventBus.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Product.API.Catalog;
using Product.API.Dtos;
using Product.API.Products;
using Product.API.Search;

namespace Product.API.Endpoints;

public static class ProductEndpoints
{
    public static void MapProductEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/products")
            .WithTags("Products");

        group.MapGet("/", async (
            [AsParameters] ProductCatalogRequest request,
            IProductCatalogService catalogService,
            ILogger<Program> logger,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var result = await catalogService.GetCatalogAsync(request, cancellationToken);

                return result.IsSuccess
                    ? Results.Ok(result.Page)
                    : Results.BadRequest(new { message = result.ErrorMessage });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "(ProductService) Lỗi khi truy vấn dữ liệu tổng hợp");
                return Results.Problem("Lỗi không thể lấy dữ liệu");
            }
        });

        group.MapGet("/categories", async (IProductReadService products, CancellationToken cancellationToken) =>
            Results.Ok(await products.GetCategoriesAsync(cancellationToken)))
        .WithName("GetCategories");

        group.MapGet("/{id:guid}", async (
            Guid id,
            IProductReadService products,
            ILogger<Program> logger,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var product = await products.GetProductByIdAsync(id, cancellationToken);

                return product is not null ? Results.Ok(product) : Results.NotFound();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "(ProductService) DB lỗi không thể truy cập");
                return Results.Problem("Lỗi không thể lấy dữ liệu products");
            }
        });

        group.MapPut("/{productId:guid}", async (
            Guid productId,
            [FromBody] UpdateProductRequest request,
            IProductAdminCommandService commands,
            CancellationToken cancellationToken) =>
        {
            var result = await commands.QueueUpdateProductAsync(productId, request, cancellationToken);
            return ToHttpResult(result);
        })
        .RequireAuthorization(EndpointHelpers.AdminOnly)
        .AddEndpointFilter<ValidationFilter<UpdateProductRequest>>()
        .AddEndpointFilter<IdempotencyFilter>();

        group.MapDelete("/{id:guid}", async (
            Guid id,
            IProductAdminCommandService commands,
            CancellationToken cancellationToken) =>
        {
            var result = await commands.QueueDeleteProductAsync(id, cancellationToken);
            return ToHttpResult(result);
        })
        .RequireAuthorization(EndpointHelpers.AdminOnly)
        .AddEndpointFilter<IdempotencyFilter>();

        group.MapPost("/categories", async (
            CategoryRequest request,
            IProductAdminCommandService commands,
            CancellationToken cancellationToken) =>
        {
            var result = await commands.CreateCategoryAsync(request, cancellationToken);
            return result.Status == ProductOperationStatus.Created && result.Payload is not null
                ? Results.Created($"/api/products/categories/{result.Payload.Id}", result.Payload)
                : ToHttpResult(result);
        })
        .AddEndpointFilter<ValidationFilter<CategoryRequest>>()
        .RequireAuthorization(EndpointHelpers.AdminOnly)
        .WithName("CreateCategory")
        .AddEndpointFilter<IdempotencyFilter>();

        group.MapPost("/", async (
            ProductRequest request,
            IProductAdminCommandService commands,
            CancellationToken cancellationToken) =>
        {
            var result = await commands.QueueCreateProductAsync(request, cancellationToken);
            return ToHttpResult(result);
        })
        .AddEndpointFilter<ValidationFilter<ProductRequest>>()
        .RequireAuthorization(EndpointHelpers.AdminOnly)
        .AddEndpointFilter<IdempotencyFilter>();

        group.MapGet("/search", async Task<IResult> (
            [AsParameters] SearchProductRequest request,
            IProductSearchService searchService,
            CancellationToken cancellationToken) =>
        {
            var result = await searchService.SearchAsync(request, cancellationToken);
            if (!result.IsSuccess)
                return Results.BadRequest(new { message = result.ErrorMessage });

            var page = result.Page!;
            return Results.Ok(new
            {
                page.TotalItems,
                page.CurrentPage,
                page.PageSize,
                page.Items
            });
        });
    }



    private static IResult ToHttpResult<T>(ProductOperationResult<T> result)
        => result.Status switch
        {
            ProductOperationStatus.Ok => Results.Ok(result.Payload),
            ProductOperationStatus.Created => Results.Created(null as string, result.Payload),
            ProductOperationStatus.Accepted => Results.Accepted(null, new
            {
                message = result.Message,
                productId = result.Payload is ProductCommandAccepted accepted ? accepted.ProductId : null
            }),
            ProductOperationStatus.BadRequest => Results.BadRequest(new { message = result.Message }),
            ProductOperationStatus.NotFound => Results.NotFound(new { message = result.Message }),
            ProductOperationStatus.Conflict => Results.Conflict(new { message = result.Message }),
            _ => Results.Problem("Product operation failed.")
        };
}
