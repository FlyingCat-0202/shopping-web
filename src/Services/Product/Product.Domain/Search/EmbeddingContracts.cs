using System.Text.Json.Serialization;

namespace Product.Domain.Search;

public interface IAiEmbeddingService
{
    Task<float[]> GetVectorAsync(string text, CancellationToken cancellationToken = default);
    Task<float[]?[]> GetVectorsAsync(string[] texts, CancellationToken cancellationToken = default);
}

public record InfinityEmbeddingResponse(
    [property: JsonPropertyName("data")] List<InfinityEmbeddingData> Data
);

public record InfinityEmbeddingData(
    [property: JsonPropertyName("index")]     int    Index,
    [property: JsonPropertyName("embedding")] float[] Embedding
);
