using EventBus.Contracts;
using Microsoft.Extensions.Options;

namespace Product.API.Search;

internal sealed class ProductSearchService(
    IProductElasticSearch elasticSearch,
    IProductDatabaseSearch databaseSearch,
    IOptions<ProductSearchOptions> options,
    ILogger<ProductSearchService> logger)
    : IProductSearchService
{
    public async Task<ProductSearchResult> SearchAsync(
        SearchProductRequest request,
        CancellationToken cancellationToken = default)
    {
        var page = ProductQueryPage.Normalize(
            request.Page,
            request.PageSize,
            options.Value.MaxPageSize);
        var limit = ProductQueryLimit.Create(options.Value.MaxOffsetItems);
        if (!limit.Allows(page))
            return ProductSearchResult.LimitExceeded(options.Value.MaxOffsetItems);

        var keyword = request.Keyword?.Trim() ?? string.Empty;
        var stock = ProductQueryPolicy.NormalizeStock(request.Stock);
        var sort = ProductQueryPolicy.NormalizeSort(request.Sort);

        if (string.IsNullOrWhiteSpace(keyword) || !options.Value.UseElastic)
            return ProductSearchResult.Success(await SearchDatabase(keyword, page, stock, sort, request, cancellationToken));

        try
        {
            var query = new ProductSearchQuery(
                keyword,
                page,
                request.CategoryId,
                stock,
                ProductQueryPolicy.ToElasticStockStatus(stock),
                sort);

            var response = await elasticSearch.SearchAsync(query, cancellationToken);

            if (options.Value.FallbackToDatabaseWhenElasticReturnsNoResults && !options.Value.RequireElastic && response.Items.Count == 0)
            {
                logger.LogWarning(
                    "Elasticsearch returned no results for keyword {Keyword}. Falling back to database search; index may be empty or still rebuilding.",
                    keyword);

                return ProductSearchResult.Success(await SearchDatabase(keyword, page, stock, sort, request, cancellationToken));
            }

            return ProductSearchResult.Success(response);
        }
        catch (Exception ex)
        {
            if (options.Value.RequireElastic)
                throw;

            logger.LogWarning(ex, "Elasticsearch search failed. Falling back to database search.");
            return ProductSearchResult.Success(await SearchDatabase(keyword, page, stock, sort, request, cancellationToken));
        }
    }

    private Task<ProductSearchPageResponse> SearchDatabase(
        string keyword,
        ProductQueryPage page,
        string stock,
        string sort,
        SearchProductRequest request,
        CancellationToken cancellationToken)
        => databaseSearch.SearchAsync(
            new ProductSearchQuery(
                keyword,
                page,
                request.CategoryId,
                stock,
                StockStatus: null,
                sort),
            cancellationToken);
}
