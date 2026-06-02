using System.Text.Json.Serialization;

namespace Product.Domain.Entities;

public interface IAiEmbeddingService
{
    Task<float[]> GetVectorAsync(string text, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gộp nhiều text thành 1 HTTP request duy nhất đến AI server.
    /// Trả về mảng vector tương ứng theo thứ tự, null nếu text đó lỗi.
    /// </summary>
    Task<float[]?[]> GetVectorsAsync(string[] texts, CancellationToken cancellationToken = default);
}

// Các class DTO dùng để parse JSON trả về từ Gemini
public record GeminiEmbeddingResponse(
    [property: JsonPropertyName("embedding")] EmbeddingData Embedding
);

public record EmbeddingData(
    [property: JsonPropertyName("values")] float[] Values
);

public record InfinityEmbeddingResponse(
    [property: JsonPropertyName("data")] List<InfinityEmbeddingData> Data
);

public record InfinityEmbeddingData(
    [property: JsonPropertyName("index")]     int    Index,
    [property: JsonPropertyName("embedding")] float[] Embedding
);
