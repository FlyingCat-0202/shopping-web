namespace EventBus.Contracts;

public record CreateProductRequest(
    string Name,
    decimal Price,
    int StockQuantity,
    int CategoryId,
    string? Description,
    string? ImageUrl,
    bool IsActive);

public record UpdateProductRequest(
    Guid Id, 
    string Name,
    decimal Price,
    int StockQuantity,
    bool IsActive,
    string? Description,
    string? ImgUrl,
    int CategoryId
);

public record DeleteProductRequest(Guid Id);


// Record định nghĩa cho docs Elastic 
public record ProductCreatedEvent(
    Guid Id,
    string Name,
    string Description,
    decimal Price,
    string CategoryName,
    bool IsActive,
    string? ImageUrl = null,
    int CategoryId = 0,
    int StockQuantity = 0
);

public record ProductDeletedEvent(Guid Id);

public record ProductUpdatedEvent(
    Guid Id,
    string Name,
    decimal Price,
    bool IsActive,
    string CategoryName,
    string? Description = null,
    string? ImageUrl = null,
    int CategoryId = 0,
    int StockQuantity = 0
);

public record SearchProductRequest(
    string Keyword,
    int Page = 1,
    int PageSize = 10,
    int? CategoryId = null,
    string? Stock = null,
    string? Sort = null
);
