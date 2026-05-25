using Elastic.Clients.Elasticsearch;

namespace Product.API.IntegrationEvents.Consumers.Elastic;

public static class ElasticConfigurator
{
    public static async Task SetupIndexAsync(ElasticsearchClient elasticClient, ILogger<Program> logger)
    {
        const string indexName = "products";

        try
        {
            var existsResponse = await elasticClient.Indices.ExistsAsync(indexName);

            // Mở comment để đập đi xây lại (Chạy 1 lần rồi comment lại)
            //if (existsResponse.Exists)
            //{
            //    await elasticClient.Indices.DeleteAsync(indexName);
            //    logger.LogWarning($"Đã xóa index cũ: {indexName}");
            //    existsResponse = await elasticClient.Indices.ExistsAsync(indexName);
            //}

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

                            // CÚ PHÁP ĐÚNG CHO DENSE VECTOR TRONG V8
                            // Tham số 1: Trỏ đến thuộc tính
                            // Tham số 2: Cấu hình chi tiết
                            .DenseVector(v => v.NameEmbeddingVector, d => d
                                .Dims(368)
                                .Index(true)
                                .Similarity("cosine")
                            )
                        )
                    )
                );

                if (!createResponse.IsValidResponse)
                {
                    logger.LogError("Không thể tạo Index: {Error}", createResponse.DebugInformation);
                    return;
                }

                logger.LogInformation("Tạo thành công Mapping Index: {Index} với Vector 3072 chiều.", indexName);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Lỗi kết nối tới Elasticsearch lúc cấu hình.");
        }
    }
}