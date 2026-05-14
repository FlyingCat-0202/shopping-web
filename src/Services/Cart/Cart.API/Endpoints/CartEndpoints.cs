using Cart.API.CartStore;
using System.Security.Claims;
using Cart.API.Dtos;
using EventBus.Extensions;
using EventBus.Infrastructure;

namespace Cart.API.Endpoints;

public static class CartEndpoints
{
    public static void MapCartEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/cart")
            .WithTags("Cart")
            .RequireAuthorization()
            .AddEndpointFilter<IdempotencyFilter>();

        group.MapGet("/", GetCart).WithName("GetCart");  // Lấy thông tin giỏ hàng hiện tại của người dùng
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
        ICartStore cartStore,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        if (!EndpointHelpers.TryGetCustomerId(user, out var customerId))
            return Results.Unauthorized();

        var cartItems = await cartStore.GetItemsAsync(customerId);

        if (cartItems.Count == 0)
            return Results.Ok(new CartSummaryResponse([], 0));

        try
        {
            var items = cartItems
                .Select(cartItem => new CartItemResponse(cartItem.ProductId, cartItem.Quantity))
                .ToList();

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
        ICartStore cartStore,
        CancellationToken cancellationToken)
    {
        if (!EndpointHelpers.TryGetCustomerId(user, out var customerId))
            return Results.Unauthorized();

        return await AddCartItem(
            cartStore,
            customerId,
            request.ProductId,
            request.Quantity,
            cancellationToken);
    }

    private static async Task<IResult> UpdateItem(
        Guid productId,
        CartItemRequest request,
        ClaimsPrincipal user,
        ICartStore cartStore,
        CancellationToken cancellationToken)
    {
        if (!EndpointHelpers.TryGetCustomerId(user, out var customerId))
            return Results.Unauthorized();

        if (request.ProductId != Guid.Empty && request.ProductId != productId)
            return Results.BadRequest("ProductId trong body không khớp với route.");

        return await UpdateExistingCartItem(
            cartStore,
            customerId,
            productId,
            request.Quantity,
            cancellationToken);
    }

    private static async Task<IResult> RemoveItem(
        Guid productId,
        ClaimsPrincipal user,
        ICartStore cartStore,
        CancellationToken cancellationToken)
    {
        if (!EndpointHelpers.TryGetCustomerId(user, out var customerId))
            return Results.Unauthorized();

        if (!await cartStore.RemoveItemAsync(customerId, productId))
            return Results.NotFound("Sản phẩm không có trong giỏ hàng.");

        return Results.Ok(new { message = "Đã xóa sản phẩm khỏi giỏ hàng.", ProductId = productId });
    }

    private static async Task<IResult> ClearCart(
        ClaimsPrincipal user,
        ICartStore cartStore,
        CancellationToken cancellationToken)
    {
        if (!EndpointHelpers.TryGetCustomerId(user, out var customerId))
            return Results.Unauthorized();

        var cartItems = await cartStore.GetItemsAsync(customerId);

        if (cartItems.Count == 0)
            return Results.Ok(new { message = "Giỏ hàng đã trống." });

        await cartStore.ClearAsync(customerId);

        return Results.Ok(new { message = "Đã xóa toàn bộ giỏ hàng." });
    }

    private static CartSummaryResponse BuildSummary(List<CartItemResponse> items)
    {
        var totalQuantity = items.Sum(item => item.Quantity);

        return new CartSummaryResponse(items, totalQuantity);
    }

    private static async Task<IResult> AddCartItem(
        ICartStore cartStore,
        Guid customerId,
        Guid productId,
        int quantity,
        CancellationToken cancellationToken)
    {
        if (quantity <= 0)
            return Results.BadRequest("Số lượng phải lớn hơn 0.");

        var currentQuantity = await cartStore.GetQuantityAsync(customerId, productId);
        var newQuantity = currentQuantity + quantity;

        await cartStore.UpsertItemAsync(customerId, productId, newQuantity);

        return Results.Ok(new
        {
            message = "Đã thêm vào giỏ hàng.",
            ProductId = productId,
            Quantity = newQuantity
        });
    }

    private static async Task<IResult> UpdateExistingCartItem(
        ICartStore cartStore,
        Guid customerId,
        Guid productId,
        int quantity,
        CancellationToken cancellationToken)
    {
        if (quantity <= 0)
            return Results.BadRequest("Số lượng phải lớn hơn 0.");

        if (!await cartStore.ItemExistsAsync(customerId, productId))
            return Results.NotFound("Sản phẩm không có trong giỏ hàng.");

        await cartStore.UpsertItemAsync(customerId, productId, quantity);

        return Results.Ok(new
        {
            message = "Đã cập nhật giỏ hàng.",
            ProductId = productId,
            Quantity = quantity
        });
    }
}
