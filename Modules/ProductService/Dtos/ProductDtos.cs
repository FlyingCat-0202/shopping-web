namespace Shopping_web.Modules.ProductService.DTOs;

public record ProductResponse(Guid Id, string Name, decimal Price, int StockQuantity, string CategoryName); // Response DTO for product details, including category name for better client-side display
public record CategoryResponse(int Id, string Name, string? Description); // Response DTO for category details, including an optional description field to provide more information about the category
public record CreateProductRequest(string Name, decimal Price, int StockQuantity, int CategoryId); // Request DTO for creating a new product, including the category ID to associate the product with a specific category