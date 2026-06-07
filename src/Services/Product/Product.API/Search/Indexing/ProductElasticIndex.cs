namespace Product.API.Search.Indexing;

public static class ElasticProductIndex
{
    // BAAI/bge-m3 produces 1024-dimensional dense vectors.
    // Bump VersionedName whenever this value or another index mapping changes.
    public const int EmbeddingDimensions = 1024;
    public const string Name = "products";
    public const string VersionedName = "products-v386";
    public const string InStock = "in-stock";
    public const string OutOfStock = "out-of-stock";

    public static string StockStatus(int stockQuantity)
        => stockQuantity > 0 ? InStock : OutOfStock;

    public static float[]? NormalizeVector(float[]? vector, ILogger logger, string context)
    {
        if (vector is null || vector.Length == 0)
            return null;

        if (vector.Length == EmbeddingDimensions)
        {
            if (vector.All(value => value == 0f))
            {
                logger.LogWarning(
                    "Bỏ qua vector embedding cho {Context} vì Elasticsearch cosine similarity không chấp nhận vector có độ lớn bằng 0.",
                    context);

                return null;
            }

            return vector;
        }

        logger.LogWarning(
            "Bỏ qua vector embedding cho {Context} vì số chiều là {ActualDimensions}, nhưng index {IndexName} yêu cầu {ExpectedDimensions}.",
            context,
            vector.Length,
            Name,
            EmbeddingDimensions);

        return null;
    }
}

public sealed record ProductEsDocument(
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
    float[]? NameEmbeddingVector = null);
