namespace Product.API.Dtos;

public record ProductResponse(
    Guid Id,
    string Name,
    decimal Price,
    int StockQuantity,
    string? Description,
    string? ImageUrl,
    int CategoryId,
    string CategoryName);

public record CategoryResponse(int Id, string Name, string? Description); // Response DTO for category details, including an optional description field to provide more information about the category
public record CategoryRequest(string Name, string? Description);
public record ProductRequest(
    string Name,
    decimal Price,
    int StockQuantity,
    string? Description,
    string? ImageUrl,
    int CategoryId,
    bool IsActive = true
);
public record ProductCategoryResponse(
    List<ProductResponse> Products,
    List<CategoryResponse> Categories
);
