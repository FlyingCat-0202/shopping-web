namespace Product.API.IntegrationEvents.Consumers.Elastic;

public record ProductEsDocument(
    Guid Id,
    string Name,
    decimal Price,
    string CategoryName,
    bool IsActive,
    string? Description = null,
    string? ImageUrl = null);
