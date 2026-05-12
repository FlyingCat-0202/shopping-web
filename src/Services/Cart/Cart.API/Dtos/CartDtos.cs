namespace Cart.API.Dtos;

public record CartItemRequest(Guid ProductId, int Quantity);

public record CartItemResponse(
    Guid ProductId,
    string ProductName,
    decimal UnitPrice,
    int Quantity,
    int StockQuantity,
    bool IsAvailable,
    decimal LineTotal);

public record CartSummaryResponse(
    List<CartItemResponse> Items,
    int TotalQuantity,
    decimal TotalAmount,
    int UnavailableItems);
