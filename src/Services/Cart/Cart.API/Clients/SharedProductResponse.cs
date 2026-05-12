namespace Cart.API.Clients;

public record SharedProductResponse(Guid Id, string Name, decimal Price, int StockQuantity);
