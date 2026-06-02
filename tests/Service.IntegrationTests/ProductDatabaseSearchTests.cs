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
        var apparel = new Category { Name = "Apparel", Description = "Jackets and coats" };
        var shoes = new Category { Name = "Shoes", Description = "Sneakers" };
        db.Categories.AddRange(apparel, shoes);
        await db.SaveChangesAsync();

        db.Products.AddRange(
            new Product.Domain.Entities.Product
            {
                Id = Guid.Parse("11111111-1111-1111-1111-111111111111"),
                Name = "100% Cotton Jacket",
                Description = "Literal percent product",
                Price = 49,
                StockQuantity = 3,
                CategoryId = apparel.Id,
                Category = apparel,
                IsActive = true
            },
            new Product.Domain.Entities.Product
            {
                Id = Guid.Parse("22222222-2222-2222-2222-222222222222"),
                Name = "Trail Jacket",
                Description = "Water resistant shell",
                Price = 89,
                StockQuantity = 7,
                CategoryId = apparel.Id,
                Category = apparel,
                IsActive = true
            },
            new Product.Domain.Entities.Product
            {
                Id = Guid.Parse("33333333-3333-3333-3333-333333333333"),
                Name = "City Sneaker",
                Description = "Daily shoe",
                Price = 99,
                StockQuantity = 5,
                CategoryId = shoes.Id,
                Category = shoes,
                IsActive = true
            });

        await db.SaveChangesAsync();
    }
}
