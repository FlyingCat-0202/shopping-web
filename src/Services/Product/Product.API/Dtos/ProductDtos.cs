namespace Product.API.Dtos;

public record ProductResponse(Guid Id, string Name, decimal Price, int StockQuantity, string? Description); // Response DTO for product details, including category name for better client-side display
public record CategoryResponse(int Id, string Name, string? Description); // Response DTO for category details, including an optional description field to provide more information about the category
public record ProductRequest(
    string Name,
    decimal Price,
    int StockQuantity,
    string? Description,
    int CategoryId,
    bool IsActive = true
);
public record ProductCategoryResponse(
    List<ProductResponse> Products,
    List<CategoryResponse> Categories
);