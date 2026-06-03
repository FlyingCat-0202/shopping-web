using Microsoft.EntityFrameworkCore;
using Product.API.Search;
using Product.Domain.Entities;
using Product.Infrastructure.Data;
using Shouldly;
using Testcontainers.PostgreSql;
using Xunit;

namespace Service.IntegrationTests;

public sealed class ProductDatabaseSearchTests
{
    [SkippableFact]
    public async Task DatabaseSearchTreatsLikeWildcardsAsLiteralKeywordCharacters()
    {
        Skip.IfNot(ServiceIntegrationTestEnvironment.IsDockerAvailable(), "Docker is required for PostgreSQL integration tests.");

        await using var postgres = new PostgreSqlBuilder()
            .WithImage("postgres:17-alpine")
            .WithDatabase("product-search-tests")
            .WithUsername("postgres")
            .WithPassword("postgres")
            .Build();

        await postgres.StartAsync();

        await using var db = CreateDbContext(postgres.GetConnectionString());
        await db.Database.MigrateAsync();
        await SeedProducts(db);

        var search = new ProductDatabaseSearch(db);
        var result = await search.SearchAsync(
            new ProductSearchQuery(
                "%",
                ProductQueryPage.Normalize(1, 12, maxPageSize: 50),
                CategoryId: null,
                ProductQueryPolicy.StockAll,
                StockStatus: null,
                ProductQueryPolicy.SortFeatured),
            CancellationToken.None);

        result.TotalItems.ShouldBe(1);
        result.Items.Single().Name.ShouldBe("100% Cotton Jacket");
    }

    [SkippableFact]
    public async Task DatabaseSearchAppliesKeywordCategoryStockAndSortConsistently()
    {
        Skip.IfNot(ServiceIntegrationTestEnvironment.IsDockerAvailable(), "Docker is required for PostgreSQL integration tests.");

        await using var postgres = new PostgreSqlBuilder()
            .WithImage("postgres:17-alpine")
            .WithDatabase("product-search-tests")
            .WithUsername("postgres")
            .WithPassword("postgres")
            .Build();

        await postgres.StartAsync();

        await using var db = CreateDbContext(postgres.GetConnectionString());
        await db.Database.MigrateAsync();
        await SeedProducts(db);

        var apparelCategoryId = await db.Categories
            .Where(c => c.Name == "Apparel")
            .Select(c => c.Id)
            .SingleAsync();

        var search = new ProductDatabaseSearch(db);
        var result = await search.SearchAsync(
            new ProductSearchQuery(
                "jacket",
                ProductQueryPage.Normalize(1, 12, maxPageSize: 50),
                apparelCategoryId,
                ProductQueryPolicy.StockInStock,
                StockStatus: null,
                ProductQueryPolicy.SortPriceAsc),
            CancellationToken.None);

        result.Items.Select(p => p.Name).ShouldBe(["100% Cotton Jacket", "Trail Jacket"]);
    }

    private static ProductDbContext CreateDbContext(string connectionString)
    {
        var options = new DbContextOptionsBuilder<ProductDbContext>()
            .UseNpgsql(connectionString, npgsql =>
            {
                npgsql.MigrationsHistoryTable("__EFMigrationsHistory_Product", "product");
            })
            .Options;

        return new ProductDbContext(options);
    }

    private static async Task SeedProducts(ProductDbContext db)
    {
        var apparel = Category.Create("Apparel", "Jackets and coats");
        var shoes = Category.Create("Shoes", "Sneakers");
        db.Categories.AddRange(apparel, shoes);
        await db.SaveChangesAsync();

        db.Products.AddRange(
            Product.Domain.Entities.Product.Create(
                "100% Cotton Jacket",
                49,
                3,
                apparel.Id,
                "Literal percent product",
                imageUrl: null,
                id: Guid.Parse("11111111-1111-1111-1111-111111111111")),
            Product.Domain.Entities.Product.Create(
                "Trail Jacket",
                89,
                7,
                apparel.Id,
                "Water resistant shell",
                imageUrl: null,
                id: Guid.Parse("22222222-2222-2222-2222-222222222222")),
            Product.Domain.Entities.Product.Create(
                "City Sneaker",
                99,
                5,
                shoes.Id,
                "Daily shoe",
                imageUrl: null,
                id: Guid.Parse("33333333-3333-3333-3333-333333333333")));

        await db.SaveChangesAsync();
    }
}
