using System.Text.Json.Serialization;

namespace Product.Domain.Entities;

public interface IAiEmbeddingService
{
    Task<float[]> GetVectorAsync(string text);
}

// Các class DTO dùng để parse JSON trả về từ Gemini
public record GeminiEmbeddingResponse(
    [property: JsonPropertyName("embedding")] EmbeddingData Embedding
);

public record EmbeddingData(
    [property: JsonPropertyName("values")] float[] Values
);