namespace Shopping_web.Modules.ProductService.DTOs;

public record ProductResponse(Guid Id, string Name, decimal Price, int StockQuantity, string CategoryName);
public record CategoryResponse(int Id, string Name, string? Description);
public record CreateProductRequest(string Name, decimal Price, int StockQuantity, int CategoryId);