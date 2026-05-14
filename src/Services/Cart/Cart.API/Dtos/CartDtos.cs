namespace Cart.API.Dtos;

public record CartItemRequest(Guid ProductId, int Quantity);

public record CartItemResponse(Guid ProductId, int Quantity);

public record CartSummaryResponse(
    List<CartItemResponse> Items,
    int TotalQuantity);
