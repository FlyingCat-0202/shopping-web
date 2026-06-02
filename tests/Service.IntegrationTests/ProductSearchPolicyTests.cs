using EventBus.Contracts;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Product.API.Dtos;
using Product.API.Search;
using Shouldly;
using Xunit;

namespace Service.IntegrationTests;

public sealed class ProductSearchPolicyTests
{
    [Theory]
    [InlineData(null, ProductQueryPolicy.SortFeatured)]
    [InlineData("", ProductQueryPolicy.SortFeatured)]
    [InlineData("featured", ProductQueryPolicy.SortFeatured)]
    [InlineData("name", ProductQueryPolicy.SortName)]
    [InlineData("PRICE-ASC", ProductQueryPolicy.SortPriceAsc)]
    [InlineData("price-desc", ProductQueryPolicy.SortPriceDesc)]
    [InlineData("nonsense", ProductQueryPolicy.SortFeatured)]
    public void NormalizesSortOptions(string? input, string expected)
        => ProductQueryPolicy.NormalizeSort(input).ShouldBe(expected);

    [Theory]
    [InlineData(null, ProductQueryPolicy.StockAll)]
    [InlineData("", ProductQueryPolicy.StockAll)]
    [InlineData("instock", ProductQueryPolicy.StockInStock)]
    [InlineData("OUTOFSTOCK", ProductQueryPolicy.StockOutOfStock)]
    [InlineData("nonsense", ProductQueryPolicy.StockAll)]
    public void NormalizesStockOptions(string? input, string expected)
        => ProductQueryPolicy.NormalizeStock(input).ShouldBe(expected);

    [Fact]
    public void EscapesDatabaseLikeWildcards()
        => ProductQueryPolicy.EscapeLikePattern(@"100% _ sale \ promo")
            .ShouldBe(@"100\% \_ sale \\ promo");

    [Fact]
    public void ProductQueryPageNormalizesPageSizeAndOffset()
    {
        var page = ProductQueryPage.Normalize(-10, 500, maxPageSize: 50);

        page.Page.ShouldBe(1);
        page.PageSize.ShouldBe(50);
        page.Offset.ShouldBe(0);
    }

    [Fact]
    public void ProductQueryLimitRejectsWindowsBeyondTheLimit()
    {
        var limit = ProductQueryLimit.Create(100);

        limit.Allows(ProductQueryPage.Normalize(8, 12, maxPageSize: 50)).ShouldBeTrue();
        limit.Allows(ProductQueryPage.Normalize(9, 12, maxPageSize: 50)).ShouldBeFalse();
    }

    [Fact]
    public async Task SearchRejectsDeepOffsetBeforeCallingEngines()
    {
        var elastic = new FakeElasticSearch();
        var database = new FakeDatabaseSearch();
        var service = CreateService(elastic, database, maxOffsetItems: 100);

        var result = await service.SearchAsync(new SearchProductRequest("jacket", Page: 100, PageSize: 12));

        result.IsSuccess.ShouldBeFalse();
        result.ErrorMessage.ShouldNotBeNull();
        elastic.Calls.ShouldBe(0);
        database.Calls.ShouldBe(0);
    }

    [Fact]
    public async Task SearchUsesDatabaseForEmptyKeyword()
    {
        var elastic = new FakeElasticSearch();
        var database = new FakeDatabaseSearch();
        var service = CreateService(elastic, database);

        var result = await service.SearchAsync(new SearchProductRequest(""));

        result.IsSuccess.ShouldBeTrue();
        elastic.Calls.ShouldBe(0);
        database.Calls.ShouldBe(1);
    }

    [Fact]
    public async Task HybridSearchFallsBackToDatabaseWhenElasticThrows()
    {
        var elastic = new FakeElasticSearch { Exception = new InvalidOperationException("elastic down") };
        var database = new FakeDatabaseSearch();
        var service = CreateService(elastic, database);

        var result = await service.SearchAsync(new SearchProductRequest("jacket"));

        result.IsSuccess.ShouldBeTrue();
        elastic.Calls.ShouldBe(1);
        database.Calls.ShouldBe(1);
    }

    [Fact]
    public async Task HybridSearchDoesNotTreatEmptyElasticResultAsFailureByDefault()
    {
        var elastic = new FakeElasticSearch();
        var database = new FakeDatabaseSearch();
        var service = CreateService(elastic, database);

        var result = await service.SearchAsync(new SearchProductRequest("jacket"));

        result.IsSuccess.ShouldBeTrue();
        result.Page!.Items.ShouldBeEmpty();
        elastic.Calls.ShouldBe(1);
        database.Calls.ShouldBe(0);
    }

    [Fact]
    public async Task HybridSearchCanFallbackWhenElasticReturnsNoResults()
    {
        var elastic = new FakeElasticSearch();
        var database = new FakeDatabaseSearch();
        var service = CreateService(
            elastic,
            database,
            configure: options => options.FallbackToDatabaseWhenElasticReturnsNoResults = true);

        var result = await service.SearchAsync(new SearchProductRequest("jacket"));

        result.IsSuccess.ShouldBeTrue();
        elastic.Calls.ShouldBe(1);
        database.Calls.ShouldBe(1);
    }

    private static ProductSearchService CreateService(
        FakeElasticSearch elastic,
        FakeDatabaseSearch database,
        int maxOffsetItems = 10_000,
        Action<MutableSearchOptions>? configure = null)
    {
        var mutable = new MutableSearchOptions
        {
            MaxOffsetItems = maxOffsetItems
        };
        configure?.Invoke(mutable);

        var options = new ProductSearchOptions
        {
            Mode = mutable.Mode,
            MaxPageSize = mutable.MaxPageSize,
            MaxOffsetItems = mutable.MaxOffsetItems,
            ElasticTimeoutSeconds = mutable.ElasticTimeoutSeconds,
            FallbackToDatabaseWhenElasticReturnsNoResults = mutable.FallbackToDatabaseWhenElasticReturnsNoResults
        };

        return new ProductSearchService(
            elastic,
            database,
            Options.Create(options),
            NullLogger<ProductSearchService>.Instance);
    }

    private sealed class MutableSearchOptions
    {
        public string Mode { get; set; } = ProductSearchMode.Hybrid;
        public int MaxPageSize { get; set; } = 50;
        public int MaxOffsetItems { get; set; } = 10_000;
        public int ElasticTimeoutSeconds { get; set; } = 4;
        public bool FallbackToDatabaseWhenElasticReturnsNoResults { get; set; }
    }

    private sealed class FakeElasticSearch : IProductElasticSearch
    {
        public int Calls { get; private set; }
        public Exception? Exception { get; init; }

        public Task<ProductSearchPageResponse> SearchAsync(ProductSearchQuery query, CancellationToken cancellationToken)
        {
            Calls++;
            if (Exception is not null)
                throw Exception;

            return Task.FromResult(new ProductSearchPageResponse([], 0, query.Page.Page, query.Page.PageSize));
        }
    }

    private sealed class FakeDatabaseSearch : IProductDatabaseSearch
    {
        public int Calls { get; private set; }

        public Task<ProductSearchPageResponse> SearchAsync(ProductSearchQuery query, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(new ProductSearchPageResponse(
                [new ProductResponse(Guid.NewGuid(), "Trail Jacket", 89.99m, 7, null, null, 1, "Outerwear")],
                1,
                query.Page.Page,
                query.Page.PageSize));
        }
    }
}
