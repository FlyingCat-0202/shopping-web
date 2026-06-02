using Elastic.Clients.Elasticsearch;
using Elastic.Clients.Elasticsearch.Mapping;
using Microsoft.EntityFrameworkCore;
using Product.Domain.Entities;
using Product.Infrastructure.Data;

namespace Product.API.IntegrationEvents.Consumers.Elastic;

public static class ElasticConfigurator
{
    public static async Task<bool> SetupIndexAsync(
        ElasticsearchClient elasticClient,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        const string indexName = ElasticProductIndex.VersionedName;

        try
        {
            var existsResponse = await elasticClient.Indices.ExistsAsync(indexName, cancellationToken);
            var created = false;

            if (!existsResponse.Exists)
            {
                var createResponse = await elasticClient.Indices.CreateAsync(indexName, c => c
                    .Mappings(m => m
                        .Properties<ProductEsDocument>(p => p
                            .Keyword(k => k.Id)
                            .Text(t => t.Name)
                            .Keyword(k => k.NameSort)
                            .Keyword(k => k.CategoryName)
                            .Boolean(b => b.IsActive)
                            .IntegerNumber(n => n.CategoryId)
                            .IntegerNumber(n => n.StockQuantity)
                            .Keyword(k => k.StockStatus)
                            .DoubleNumber(n => n.Price)
                            .Text(t => t.Description)
                            .Keyword(k => k.ImageUrl)
                            .DenseVector(v => v.NameEmbeddingVector, d => d
                                .Dims(ElasticProductIndex.EmbeddingDimensions)
                                .Index(true)
                                .Similarity(DenseVectorSimilarity.Cosine)
                            )
                        )
                    )
                , cancellationToken);

                if (!createResponse.IsValidResponse)
                {
                    logger.LogError("Không thể tạo Index: {Error}", createResponse.DebugInformation);
                    return false;
                }

                logger.LogInformation(
                    "Tạo thành công mapping index {Index} với vector {Dimensions} chiều.",
                    indexName,
                    ElasticProductIndex.EmbeddingDimensions);

                created = true;
            }

            await PointAliasToVersionedIndexAsync(elasticClient, logger, cancellationToken);
            return created;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Lỗi kết nối tới Elasticsearch lúc cấu hình.");
            return false;
        }
    }

    public static async Task RebuildIndexFromDatabaseAsync(
        ProductDbContext db,
        ElasticsearchClient elasticClient,
        IAiEmbeddingService aiEmbeddingService,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        const int batchSize = 100;
        var page = 0;
        var totalIndexed = 0;

        while (true)
        {
            var products = await db.Products
                .AsNoTracking()
                .OrderBy(p => p.Id)
                .Skip(page * batchSize)
                .Take(batchSize)
                .Select(p => new
                {
                    p.Id,
                    p.Name,
                    p.Price,
                    p.CategoryId,
                    CategoryName = p.Category.Name,
                    p.IsActive,
                    p.StockQuantity,
                    p.Description,
                    p.ImageUrl
                })
                .ToListAsync(cancellationToken);

            if (products.Count == 0)
                break;

            var vectors = await aiEmbeddingService.GetVectorsAsync(
                products.Select(p => p.Name).ToArray(),
                cancellationToken);
            var documents = products.Select((product, index) => new ProductEsDocument(
                Id: product.Id,
                Name: product.Name,
                Price: product.Price,
                CategoryName: product.CategoryName,
                IsActive: product.IsActive,
                CategoryId: product.CategoryId,
                StockQuantity: product.StockQuantity,
                StockStatus: ElasticProductIndex.StockStatus(product.StockQuantity),
                NameSort: product.Name,
                Description: product.Description,
                ImageUrl: product.ImageUrl,
                NameEmbeddingVector: ElasticProductIndex.NormalizeVector(vectors[index], logger, $"product {product.Id}")))
                .ToList();

            var bulkResponse = await elasticClient.IndexManyAsync(documents, ElasticProductIndex.VersionedName, cancellationToken);
            if (bulkResponse.Errors)
            {
                foreach (var item in bulkResponse.ItemsWithErrors)
                    logger.LogError("Rebuild Elastic index lỗi tại doc {Id}: {Error}", item.Id, item.Error?.Reason);

                throw new InvalidOperationException($"Rebuild Elastic index có {bulkResponse.ItemsWithErrors.Count()} lỗi.");
            }

            totalIndexed += documents.Count;
            page++;
        }

        logger.LogInformation(
            "Đã rebuild {Count} sản phẩm vào Elasticsearch index {Index}.",
            totalIndexed,
            ElasticProductIndex.VersionedName);
    }

    private static async Task PointAliasToVersionedIndexAsync(
        ElasticsearchClient elasticClient,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        var deleteAliasResponse = await elasticClient.Indices.DeleteAliasAsync(
            "products-*",
            ElasticProductIndex.Name,
            cancellationToken);

        if (!deleteAliasResponse.IsValidResponse &&
            deleteAliasResponse.ApiCallDetails.HttpStatusCode is not 404)
        {
            logger.LogWarning(
                "Không thể xóa alias Elasticsearch cũ {Alias}: {Details}",
                ElasticProductIndex.Name,
                deleteAliasResponse.DebugInformation);
        }

        var putAliasResponse = await elasticClient.Indices.PutAliasAsync(
            ElasticProductIndex.VersionedName,
            ElasticProductIndex.Name,
            cancellationToken);

        if (!putAliasResponse.IsValidResponse)
        {
            logger.LogError(
                "Không thể trỏ alias Elasticsearch {Alias} tới {Index}: {Details}",
                ElasticProductIndex.Name,
                ElasticProductIndex.VersionedName,
                putAliasResponse.DebugInformation);
            return;
        }

        logger.LogInformation(
            "Alias Elasticsearch {Alias} đang trỏ tới {Index}.",
            ElasticProductIndex.Name,
            ElasticProductIndex.VersionedName);
    }
}
