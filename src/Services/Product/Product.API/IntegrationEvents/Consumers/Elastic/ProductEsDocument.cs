namespace Product.API.IntegrationEvents.Consumers.Elastic;

public record ProductEsDocument(
    Guid Id,
    string Name,
    decimal Price,
    string CategoryName,
    bool IsActive,
    int CategoryId = 0,
    int StockQuantity = 0,
    string? StockStatus = null,
    string? NameSort = null,
    string? Description = null,
    string? ImageUrl = null,
    float[]? NameEmbeddingVector = null
);
