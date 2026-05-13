using System.Security.Claims;
using Cart.API.Clients;
using Cart.API.Dtos;
using EventBus.Extensions;
using Cart.Domain.Entities;
using Cart.Infrastructure.Data;
using EventBus.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Cart.API.Endpoints;

public static class CartEndpoints
{
    public static void MapCartEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/cart")
            .WithTags("Cart")
            .RequireAuthorization()
            .AddEndpointFilter<IdempotencyFilter>();

        group.MapGet("/", GetCart).WithName("GetCart");
        group.MapPost("/items", AddItem)
            .AddEndpointFilter<ValidationFilter<CartItemRequest>>()
            .WithName("AddCartItem");
        group.MapPut("/items/{productId:guid}", UpdateItem)
            .AddEndpointFilter<ValidationFilter<CartItemRequest>>()
            .WithName("UpdateCartItem");
        group.MapDelete("/items/{productId:guid}", RemoveItem).WithName("RemoveCartItem");
        group.MapDelete("/clear", ClearCart).WithName("ClearCart");
    }

    private static async Task<IResult> GetCart(
        ClaimsPrincipal user,
        CartDbContext db,
        IProductCatalogClient productClient,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        if (!EndpointHelpers.TryGetCustomerId(user, out var customerId))
            return Results.Unauthorized();

        var cartItems = await db.CartItems
            .AsNoTracking()
            .Where(c => c.CustomerId == customerId)
            .ToListAsync(cancellationToken);

        if (cartItems.Count == 0)
            return Results.Ok(new CartSummaryResponse([], 0, 0m, 0));

        try
        {
            var products = await productClient.GetProductsByIdsAsync(
                cartItems.Select(c => c.ProductId),
                cancellationToken);
            var productById = products.ToDictionary(p => p.Id);

            var items = cartItems.Select(cartItem =>
            {
                var isAvailable = productById.TryGetValue(cartItem.ProductId, out var product)
                    && product.StockQuantity > 0;

                return new CartItemResponse(
                    cartItem.ProductId,
                    isAvailable ? product!.Name : "Sản phẩm không còn khả dụng",
                    isAvailable ? product!.Price : 0m,
                    cartItem.Quantity,
                    isAvailable ? product!.StockQuantity : 0,
                    isAvailable,
                    isAvailable ? product!.Price * cartItem.Quantity : 0m);
            }).ToList();

            return Results.Ok(BuildSummary(items));
        }
        catch (Exception ex)
        {
            var logger = loggerFactory.CreateLogger("CartEndpoints");
            logger.LogError(ex, "(CartService) Lỗi khi tải giỏ hàng");
            return Results.Problem("Không thể tải giỏ hàng.");
        }
    }

    private static async Task<IResult> AddItem(
        CartItemRequest request,
        ClaimsPrincipal user,
        CartDbContext db,
        IProductCatalogClient productClient,
        CancellationToken cancellationToken)
    {
        if (!EndpointHelpers.TryGetCustomerId(user, out var customerId))
            return Results.Unauthorized();

        return await UpsertCartItem(
            db,
            productClient,
            customerId,
            request.ProductId,
            request.Quantity,
            increment: true,
            cancellationToken);
    }

    private static async Task<IResult> UpdateItem(
        Guid productId,
        CartItemRequest request,
        ClaimsPrincipal user,
        CartDbContext db,
        IProductCatalogClient productClient,
        CancellationToken cancellationToken)
    {
        if (!EndpointHelpers.TryGetCustomerId(user, out var customerId))
            return Results.Unauthorized();

        if (request.ProductId != Guid.Empty && request.ProductId != productId)
            return Results.BadRequest("ProductId trong body không khớp với route.");

        return await UpsertCartItem(
            db,
            productClient,
            customerId,
            productId,
            request.Quantity,
            increment: false,
            cancellationToken);
    }

    private static async Task<IResult> RemoveItem(
        Guid productId,
        ClaimsPrincipal user,
        CartDbContext db,
        CancellationToken cancellationToken)
    {
        if (!EndpointHelpers.TryGetCustomerId(user, out var customerId))
            return Results.Unauthorized();

        var cartItem = await db.CartItems.FirstOrDefaultAsync(
            c => c.CustomerId == customerId && c.ProductId == productId,
            cancellationToken);

        if (cartItem is null)
            return Results.NotFound("Sản phẩm không có trong giỏ hàng.");

        db.CartItems.Remove(cartItem);
        await db.SaveChangesAsync(cancellationToken);

        return Results.Ok(new { message = "Đã xóa sản phẩm khỏi giỏ hàng.", ProductId = productId });
    }

    private static async Task<IResult> ClearCart(
        ClaimsPrincipal user,
        CartDbContext db,
        CancellationToken cancellationToken)
    {
        if (!EndpointHelpers.TryGetCustomerId(user, out var customerId))
            return Results.Unauthorized();

        var cartItems = await db.CartItems
            .Where(c => c.CustomerId == customerId)
            .ToListAsync(cancellationToken);

        if (cartItems.Count == 0)
            return Results.Ok(new { message = "Giỏ hàng đã trống." });

        db.CartItems.RemoveRange(cartItems);
        await db.SaveChangesAsync(cancellationToken);

        return Results.Ok(new { message = "Đã xóa toàn bộ giỏ hàng." });
    }

    private static CartSummaryResponse BuildSummary(List<CartItemResponse> items)
    {
        var totalQuantity = items.Sum(item => item.Quantity);
        var totalAmount = items.Sum(item => item.LineTotal);
        var unavailableItems = items.Count(item => !item.IsAvailable);

        return new CartSummaryResponse(items, totalQuantity, totalAmount, unavailableItems);
    }

    private static async Task<IResult> UpsertCartItem(
        CartDbContext db,
        IProductCatalogClient productClient,
        Guid customerId,
        Guid productId,
        int quantity,
        bool increment,
        CancellationToken cancellationToken)
    {
        if (quantity <= 0)
            return Results.BadRequest("Số lượng phải lớn hơn 0.");

        var product = (await productClient.GetProductsByIdsAsync([productId], cancellationToken)).FirstOrDefault();
        if (product is null)
            return Results.NotFound("Sản phẩm không tồn tại hoặc đã ngừng kinh doanh.");

        if (product.StockQuantity <= 0)
            return Results.Conflict("Sản phẩm hiện đã hết hàng.");

        var cartItem = await db.CartItems.FirstOrDefaultAsync(
            c => c.CustomerId == customerId && c.ProductId == productId,
            cancellationToken);

        var newQuantity = increment ? (cartItem?.Quantity ?? 0) + quantity : quantity;
        if (newQuantity > product.StockQuantity)
            return Results.Conflict($"Chỉ còn {product.StockQuantity} sản phẩm trong kho.");

        if (cartItem is null)
            db.CartItems.Add(new CartItem(customerId, productId, newQuantity));
        else
            cartItem.UpdateQuantity(newQuantity);

        await db.SaveChangesAsync(cancellationToken);

        return Results.Ok(new
        {
            message = increment ? "Đã thêm vào giỏ hàng." : "Đã cập nhật giỏ hàng.",
            ProductId = productId,
            Quantity = newQuantity
        });
    }
}
