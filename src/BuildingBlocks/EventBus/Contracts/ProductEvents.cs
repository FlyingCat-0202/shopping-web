namespace EventBus.Contracts;

public record CreateProductRequest(
    string Name,
    decimal Price,
    int StockQuantity,
    int CategoryId,
    string? Description,
    string? ImageUrl,
    bool IsActive);
public record UpdateProductRequest(Guid Id, int CategoryId);
public record DeleteProductRequest(Guid Id);
