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

public record CategoryResponse(int Id, string Name, string? Description);
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

public sealed class ProductCatalogRequest
{
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 12;
    public int? CategoryId { get; init; }
    public string? Stock { get; init; }
    public string? Sort { get; init; }
}

public record ProductCategoryPageResponse(
    List<ProductResponse> Products,
    List<CategoryResponse> Categories,
    int TotalItems,
    int CurrentPage,
    int PageSize
);
