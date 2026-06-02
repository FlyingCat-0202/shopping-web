using Elastic.Clients.Elasticsearch;
using Microsoft.Extensions.Options;
using Product.API.Search;
using Product.Domain.Entities;
using Product.Infrastructure.Data;

namespace Product.API.IntegrationEvents.Consumers.Elastic;

internal sealed class ElasticIndexStartupService(
    IServiceScopeFactory scopeFactory,
    IOptions<ProductSearchOptions> options,
    ILogger<ElasticIndexStartupService> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.ConfigureElasticIndexOnStartup)
        {
            logger.LogInformation("Skipping Elasticsearch startup index setup because ProductSearch configuration disabled it.");
            return;
        }

        await using var scope = scopeFactory.CreateAsyncScope();
        var elasticClient = scope.ServiceProvider.GetRequiredService<ElasticsearchClient>();
        var db = scope.ServiceProvider.GetRequiredService<ProductDbContext>();
        var aiEmbeddingService = scope.ServiceProvider.GetRequiredService<IAiEmbeddingService>();

        try
        {
            var createdIndex = await ElasticConfigurator.SetupIndexAsync(elasticClient, logger, stoppingToken);
            if (!createdIndex || !options.Value.RebuildElasticIndexWhenCreated)
                return;

            await ElasticConfigurator.RebuildIndexFromDatabaseAsync(
                db,
                elasticClient,
                aiEmbeddingService,
                logger,
                stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            logger.LogInformation("Elasticsearch startup indexing was cancelled.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Elasticsearch startup indexing failed. Product catalog remains available; search can fall back to database.");
        }
    }
}
