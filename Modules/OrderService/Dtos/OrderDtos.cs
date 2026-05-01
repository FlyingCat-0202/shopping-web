namespace Shopping_web.Modules.OrderService.Dtos;
public record AddToCartRequest(Guid ProductId, int Quantity);
public record CartResponse(Guid ProductId, string ProductName, int Quantity, decimal UnitPrice);
public record CheckoutRequest(string PaymentMethod);