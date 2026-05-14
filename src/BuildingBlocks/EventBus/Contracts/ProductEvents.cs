namespace EventBus.Contracts;

public record CreateProductRequest(string Name, decimal Price, int StockQuantity, int CategoryId);
public record UpdateProductRequest(Guid Id, int CategoryId);
public record DeleteProductRequest(Guid Id);
