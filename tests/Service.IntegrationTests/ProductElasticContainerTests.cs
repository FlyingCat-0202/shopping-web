using DotNet.Testcontainers.Builders;
using Elastic.Clients.Elasticsearch;
using Microsoft.Extensions.Logging.Abstractions;
using Product.API.Search.Indexing;
using Shouldly;
using Xunit;

namespace Service.IntegrationTests;

public class ProductElasticContainerTests
{
    [SkippableFact]
    public async Task ElasticIndexManagerCreatesVersionedIndexAliasAndQueryableFields()
    {
        Skip.IfNot(ServiceIntegrationTestEnvironment.IsDockerAvailable(), "Docker is required for Elasticsearch integration tests.");

        await using var elasticsearch = new ContainerBuilder()
            .WithImage("docker.elastic.co/elasticsearch/elasticsearch:8.17.6")
            .WithEnvironment("discovery.type", "single-node")
            .WithEnvironment("xpack.security.enabled", "false")
            .WithEnvironment("ES_JAVA_OPTS", "-Xms512m -Xmx512m")
            .WithPortBinding(9200, true)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilInternalTcpPortIsAvailable(9200))
            .Build();

        await elasticsearch.StartAsync();

        var client = new ElasticsearchClient(new ElasticsearchClientSettings(
            new Uri($"http://{elasticsearch.Hostname}:{elasticsearch.GetMappedPublicPort(9200)}"))
            .DefaultIndex(ElasticProductIndex.Name));

        await WaitForElasticsearchAsync(client);

        var created = await ProductElasticIndexManager.SetupIndexAsync(client, NullLogger.Instance);
        var createdAgain = await ProductElasticIndexManager.SetupIndexAsync(client, NullLogger.Instance);

        created.ShouldBeTrue();
        createdAgain.ShouldBeFalse();

        var indexExists = await client.Indices.ExistsAsync(ElasticProductIndex.VersionedName);
        indexExists.Exists.ShouldBeTrue();

        var productId = Guid.NewGuid();
        var document = new ProductEsDocument(
            Id: productId,
            Name: "Trail Jacket",
            Price: 89.99m,
            CategoryName: "Outerwear",
            IsActive: true,
            CategoryId: 42,
            StockQuantity: 7,
            StockStatus: ElasticProductIndex.InStock,
            NameSort: "Trail Jacket",
            Description: "Water resistant shell",
            ImageUrl: "https://cdn.test/trail-jacket.png",
            NameEmbeddingVector: Enumerable.Repeat(0.1f, ElasticProductIndex.EmbeddingDimensions).ToArray());

        var indexResponse = await client.IndexAsync(document, ElasticProductIndex.VersionedName);
        indexResponse.IsValidResponse.ShouldBeTrue(indexResponse.DebugInformation);

        await client.Indices.RefreshAsync(ElasticProductIndex.VersionedName);

        var searchResponse = await client.SearchAsync<ProductEsDocument>(s => s
            .Indices(ElasticProductIndex.Name)
            .Size(10)
            .Query(q => q.Bool(b => b
                .Must(m => m.Match(mm => mm.Field(p => p.Name).Query("trail jacket")))
                .Filter(
                    f => f.Term(t => t.Field(p => p.CategoryId).Value(42)),
                    f => f.Term(t => t.Field(p => p.StockStatus).Value(ElasticProductIndex.InStock)),
                    f => f.Term(t => t.Field(p => p.IsActive).Value(true))))));

        searchResponse.IsValidResponse.ShouldBeTrue(searchResponse.DebugInformation);
        searchResponse.Documents.ShouldContain(p => p.Id == productId);
    }

    private static async Task WaitForElasticsearchAsync(ElasticsearchClient client)
    {
        var deadline = DateTime.UtcNow.AddSeconds(45);
        Exception? lastError = null;

        while (DateTime.UtcNow < deadline)
        {
            try
            {
                var info = await client.InfoAsync();
                if (info.IsValidResponse)
                    return;

                lastError = new InvalidOperationException(info.DebugInformation);
            }
            catch (Exception ex)
            {
                lastError = ex;
            }

            await Task.Delay(TimeSpan.FromSeconds(1));
        }

        throw new TimeoutException("Elasticsearch did not become ready in time.", lastError);
    }
}
