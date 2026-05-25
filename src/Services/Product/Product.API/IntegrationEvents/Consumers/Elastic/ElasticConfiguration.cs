using Elastic.Clients.Elasticsearch;
using Elastic.Clients.Elasticsearch.Mapping;

namespace Product.API.IntegrationEvents.Consumers.Elastic;

public static class ElasticConfigurator
{
    public static async Task SetupIndexAsync(ElasticsearchClient elasticClient, ILogger<Program> logger)
    {
        const string indexName = ElasticProductIndex.Name;

        try
        {
            var existsResponse = await elasticClient.Indices.ExistsAsync(indexName);

            if (!existsResponse.Exists)
            {
                var createResponse = await elasticClient.Indices.CreateAsync(indexName, c => c
                    .Mappings(m => m
                        .Properties<ProductEsDocument>(p => p
                            .Keyword(k => k.Id)
                            .Text(t => t.Name)
                            .Keyword(k => k.CategoryName)
                            .Boolean(b => b.IsActive)
                            .Text(t => t.Description)
                            .Keyword(k => k.ImageUrl)
                            .DenseVector(v => v.NameEmbeddingVector, d => d
                                .Dims(ElasticProductIndex.EmbeddingDimensions)
                                .Index(true)
                                .Similarity(DenseVectorSimilarity.Cosine)
                            )
                        )
                    )
                );

                if (!createResponse.IsValidResponse)
                {
                    logger.LogError("Không thể tạo Index: {Error}", createResponse.DebugInformation);
                    return;
                }

                logger.LogInformation(
                    "Tạo thành công mapping index {Index} với vector {Dimensions} chiều.",
                    indexName,
                    ElasticProductIndex.EmbeddingDimensions);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Lỗi kết nối tới Elasticsearch lúc cấu hình.");
        }
    }
}
