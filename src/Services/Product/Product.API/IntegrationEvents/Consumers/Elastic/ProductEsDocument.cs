namespace Product.API.IntegrationEvents.Consumers.Elastic;

// Record này đại diện cho MỌI THỨ bạn muốn Frontend nhìn thấy khi Search
public record ProductEsDocument(
    Guid Id,
    string Name,
    decimal Price,
    string CategoryName,
    bool IsActive,
    string? Description = null, // Có thể null
    string? ImageUrl = null     // Có thể null
);