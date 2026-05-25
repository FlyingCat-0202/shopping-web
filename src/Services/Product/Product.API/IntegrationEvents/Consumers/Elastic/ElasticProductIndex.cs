namespace Product.API.IntegrationEvents.Consumers.Elastic;

public static class ElasticProductIndex
{
    public const int EmbeddingDimensions = 384;
    public const string Name = "products-v384";

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
