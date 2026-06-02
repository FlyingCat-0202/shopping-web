using EventBus.Contracts;
using Microsoft.Extensions.Options;

namespace Product.API.Search;

internal sealed class ProductSearchService(
    IProductElasticSearch elasticSearch,
    IProductDatabaseSearch databaseSearch,
    IProductSearchCategoryResolver categoryResolver,
    IOptions<ProductSearchOptions> options,
    ILogger<ProductSearchService> logger)
    : IProductSearchService
{
    public async Task<ProductSearchPageResponse> SearchAsync(
        SearchProductRequest request,
        CancellationToken cancellationToken = default)
    {
        var page = Math.Max(request.Page, 1);
        var pageSize = Math.Clamp(request.PageSize, 1, 50);
        var keyword = request.Keyword?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(keyword) || !options.Value.UseElastic)
            return await SearchDatabase(keyword, page, pageSize, request, cancellationToken);

        try
        {
            var categoryName = await categoryResolver.ResolveCategoryNameAsync(request, cancellationToken);
            var query = new ProductSearchQuery(
                keyword,
                page,
                pageSize,
                request.CategoryId,
                categoryName,
                request.Stock,
                ProductSearchFilters.NormalizeElasticStockStatus(request.Stock),
                request.Sort);

            var response = await elasticSearch.SearchAsync(query, cancellationToken);

            if (!options.Value.RequireElastic && response.Items.Count == 0)
            {
                logger.LogWarning(
                    "Elasticsearch returned no results for keyword {Keyword}. Falling back to database search; index may be empty or still rebuilding.",
                    keyword);

                return await SearchDatabase(keyword, page, pageSize, request, cancellationToken);
            }

            return response;
        }
        catch (Exception ex)
        {
            if (options.Value.RequireElastic)
                throw;

            logger.LogWarning(ex, "Elasticsearch search failed. Falling back to database search.");
            return await SearchDatabase(keyword, page, pageSize, request, cancellationToken);
        }
    }

    private Task<ProductSearchPageResponse> SearchDatabase(
        string keyword,
        int page,
        int pageSize,
        SearchProductRequest request,
        CancellationToken cancellationToken)
        => databaseSearch.SearchAsync(
            new ProductSearchQuery(
                keyword,
                page,
                pageSize,
                request.CategoryId,
                CategoryName: null,
                request.Stock,
                StockStatus: null,
                request.Sort),
            cancellationToken);
}
