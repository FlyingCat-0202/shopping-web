namespace Product.API.IntegrationEvents.Consumers.Elastic;

public static class ElasticProductIndex
{
    public const int EmbeddingDimensions = 384;
    public const string Name = "products";
    public const string VersionedName = "products-v385";
    public const string InStock = "in-stock";
    public const string OutOfStock = "out-of-stock";

    public static string StockStatus(int stockQuantity)
        => stockQuantity > 0 ? InStock : OutOfStock;

    public static float[]? NormalizeVector(float[]? vector, ILogger logger, string context)
    {
        if (vector is null || vector.Length == 0)
            return null;

        if (vector.Length == EmbeddingDimensions)
            return vector;

        logger.LogWarning(
            "Bỏ qua vector embedding cho {Context} vì số chiều là {ActualDimensions}, nhưng index {IndexName} yêu cầu {ExpectedDimensions}.",
            context,
            vector.Length,
            Name,
            EmbeddingDimensions);

        return null;
    }
}
