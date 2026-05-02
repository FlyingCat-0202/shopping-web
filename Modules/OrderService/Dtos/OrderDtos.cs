namespace Shopping_web.Modules.OrderService.Dtos;
public record AddToCartRequest(Guid ProductId, int Quantity); // Request DTO for adding a product to the cart
public record CartResponse(Guid ProductId, string ProductName, int Quantity, decimal UnitPrice); // Response DTO for cart items, including product details and pricing
public record CheckoutRequest(string PaymentMethod); // Request DTO for checking out the cart, including payment method information