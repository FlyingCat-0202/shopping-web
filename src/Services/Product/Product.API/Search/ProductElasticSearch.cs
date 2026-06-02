using Elastic.Clients.Elasticsearch;
using Elastic.Clients.Elasticsearch.QueryDsl;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Product.API.Dtos;
using Product.API.IntegrationEvents.Consumers.Elastic;
using Product.Domain.Entities;
using Product.Infrastructure.Data;

namespace Product.API.Search;

internal interface IProductElasticSearch
{
    Task<ProductSearchPageResponse> SearchAsync(ProductSearchQuery query, CancellationToken cancellationToken);
}

internal sealed class ProductElasticSearch(
    ProductDbContext db,
    ElasticsearchClient elasticClient,
    IAiEmbeddingService aiEmbeddingService,
    IOptions<ProductSearchOptions> options,
    ILogger<ProductElasticSearch> logger)
    : IProductElasticSearch
{
    public async Task<ProductSearchPageResponse> SearchAsync(
        ProductSearchQuery query,
        CancellationToken cancellationToken)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, options.Value.ElasticTimeoutSeconds)));

        var from = (query.Page - 1) * query.PageSize;
        var queryVector = await aiEmbeddingService.GetVectorAsync(query.Keyword, timeoutCts.Token);
        var normalizedQueryVector = ElasticProductIndex.NormalizeVector(queryVector, logger, $"search keyword '{query.Keyword}'");
        var useVectorSearch = normalizedQueryVector is not null;
        var vectorResultCount = Math.Min(Math.Max(from + query.PageSize, query.PageSize), 1000);
        var vectorCandidateCount = Math.Min(Math.Max(vectorResultCount * 5, 100), 10_000);
        var filters = BuildElasticFilters(query.CategoryName, query.StockStatus);

        var searchResponse = await elasticClient.SearchAsync<ProductEsDocument>(s =>
        {
            var descriptor = s
                .Indices(ElasticProductIndex.Name)
                .From(from)
                .Size(query.PageSize)
                .Query(q => q.Bool(b =>
                {
                    if (useVectorSearch)
                    {
                        b.Should(
                            sh => sh.Knn(k => k
                                .Field(f => f.NameEmbeddingVector)
                                .QueryVector(normalizedQueryVector!)
                                .K(vectorResultCount)
                                .NumCandidates(vectorCandidateCount)
                                .Filter(filters)),
                            sh => sh.MultiMatch(mm => mm
                                .Query(query.Keyword)
                                .Fields(new[] { "name^5", "categoryName" })
                                .Fuzziness(new Fuzziness("AUTO"))));
                    }
                    else
                    {
                        b.Should(sh => sh.MultiMatch(mm => mm
                            .Query(query.Keyword)
                            .Fields(new[] { "name^5", "categoryName" })
                            .Fuzziness(new Fuzziness("AUTO"))));
                    }

                    b.Filter(filters);
                    b.MinimumShouldMatch(1);
                }));

            ApplyElasticSort(descriptor, query.Sort);
        }, timeoutCts.Token);

        if (!searchResponse.IsValidResponse)
        {
            throw new InvalidOperationException(searchResponse.DebugInformation);
        }

        var documents = searchResponse.Documents.ToList();
        var productIds = documents.Select(doc => doc.Id).ToList();
        var productsById = await db.Products
            .AsNoTracking()
            .Where(p => productIds.Contains(p.Id))
            .Select(p => new ProductResponse(
                p.Id,
                p.Name,
                p.Price,
                p.StockQuantity,
                p.Description,
                p.ImageUrl,
                p.CategoryId,
                p.Category.Name))
            .ToDictionaryAsync(p => p.Id, cancellationToken);

        var items = documents
            .Select(doc => productsById.TryGetValue(doc.Id, out var product)
                ? product
                : new ProductResponse(
                    doc.Id,
                    doc.Name,
                    doc.Price,
                    doc.StockQuantity,
                    doc.Description,
                    doc.ImageUrl,
                    doc.CategoryId,
                    doc.CategoryName))
            .ToList();

        return new ProductSearchPageResponse(items, searchResponse.Total, query.Page);
    }

    private static Action<QueryDescriptor<ProductEsDocument>>[] BuildElasticFilters(
        string? categoryName,
        string? stockStatus)
    {
        var filters = new List<Action<QueryDescriptor<ProductEsDocument>>>
        {
            f => f.Term(t => t.Field(p => p.IsActive).Value(true))
        };

        if (!string.IsNullOrWhiteSpace(categoryName))
            filters.Add(f => f.Term(t => t.Field(p => p.CategoryName).Value(categoryName)));

        if (!string.IsNullOrWhiteSpace(stockStatus))
            filters.Add(f => f.Term(t => t.Field(p => p.StockStatus).Value(stockStatus)));

        return filters.ToArray();
    }

    private static void ApplyElasticSort(
        SearchRequestDescriptor<ProductEsDocument> descriptor,
        string? sort)
    {
        switch (sort?.Trim().ToLowerInvariant())
        {
            case "price-asc":
                descriptor.Sort(s => s.Field(f => f.Field(p => p.Price).Order(SortOrder.Asc)));
                break;
            case "price-desc":
                descriptor.Sort(s => s.Field(f => f.Field(p => p.Price).Order(SortOrder.Desc)));
                break;
            case "name":
                descriptor.Sort(s => s.Field(f => f.Field(p => p.NameSort).Order(SortOrder.Asc)));
                break;
        }
    }
}
