using EventBus.Contracts;
using Microsoft.EntityFrameworkCore;
using Product.API.IntegrationEvents.Consumers.OrderSupportConsumer;
using Product.Domain.Entities;
using Product.Infrastructure.Data;
using Shouldly;
using Xunit;

namespace Service.IntegrationTests;

public sealed class ProductStockReservationServiceTests
{
    [Fact]
    public async Task ReserveStockDeductsStockAndCreatesSnapshotReservations()
    {
        await using var db = CreateInMemoryDbContext();
        var product = await SeedProductAsync(db, stockQuantity: 5, price: 89);
        var service = new StockReservationService(db);

        var result = await service.ReserveStockAsync(
            Guid.NewGuid(),
            [new OrderItemInfo { ProductId = product.Id, Quantity = 2 }],
            CancellationToken.None);
        await db.SaveChangesAsync();

        result.Status.ShouldBe(StockReservationResultStatus.Reserved);
        result.Items.Single().ProductName.ShouldBe("Trail Jacket");
        product.StockQuantity.ShouldBe(3);

        var reservation = await db.StockReservations.SingleAsync();
        reservation.ProductName.ShouldBe("Trail Jacket");
        reservation.UnitPrice.ShouldBe(89);
    }

    [Fact]
    public async Task ReserveStockDuplicateCommandReturnsExistingSnapshotWithoutDeductingTwice()
    {
        await using var db = CreateInMemoryDbContext();
        var product = await SeedProductAsync(db, stockQuantity: 5, price: 89);
        var orderId = Guid.NewGuid();
        var service = new StockReservationService(db);

        await service.ReserveStockAsync(
            orderId,
            [new OrderItemInfo { ProductId = product.Id, Quantity = 2 }],
            CancellationToken.None);
        await db.SaveChangesAsync();

        product.Update(
            "Renamed Later",
            199,
            product.StockQuantity,
            product.CategoryId,
            product.Description,
            product.ImageUrl,
            product.IsActive);
        await db.SaveChangesAsync();

        var duplicate = await service.ReserveStockAsync(
            orderId,
            [new OrderItemInfo { ProductId = product.Id, Quantity = 2 }],
            CancellationToken.None);

        duplicate.Status.ShouldBe(StockReservationResultStatus.Reserved);
        duplicate.Items.Single().ProductName.ShouldBe("Trail Jacket");
        duplicate.Items.Single().UnitPrice.ShouldBe(89);
        product.StockQuantity.ShouldBe(3);
        (await db.StockReservations.CountAsync()).ShouldBe(1);
    }

    [Fact]
    public async Task ReserveStockFailsWhenProductIsMissingOrInactive()
    {
        await using var db = CreateInMemoryDbContext();
        var service = new StockReservationService(db);

        var result = await service.ReserveStockAsync(
            Guid.NewGuid(),
            [new OrderItemInfo { ProductId = Guid.NewGuid(), Quantity = 1 }],
            CancellationToken.None);

        result.Status.ShouldBe(StockReservationResultStatus.Failed);
        result.FailureReason!.ShouldContain("Sản phẩm không tồn tại");
        (await db.StockReservations.CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task ReserveStockFailsWhenQuantityIsInvalidOrInsufficient()
    {
        await using var db = CreateInMemoryDbContext();
        var product = await SeedProductAsync(db, stockQuantity: 1, price: 89);
        var service = new StockReservationService(db);

        var invalidQuantity = await service.ReserveStockAsync(
            Guid.NewGuid(),
            [new OrderItemInfo { ProductId = product.Id, Quantity = 0 }],
            CancellationToken.None);

        var insufficientStock = await service.ReserveStockAsync(
            Guid.NewGuid(),
            [new OrderItemInfo { ProductId = product.Id, Quantity = 2 }],
            CancellationToken.None);

        invalidQuantity.Status.ShouldBe(StockReservationResultStatus.Failed);
        insufficientStock.Status.ShouldBe(StockReservationResultStatus.Failed);
        product.StockQuantity.ShouldBe(1);
        (await db.StockReservations.CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task ReleaseReservedStockRestoresStockAndIsIdempotent()
    {
        await using var db = CreateInMemoryDbContext();
        var product = await SeedProductAsync(db, stockQuantity: 5, price: 89);
        var orderId = Guid.NewGuid();
        var service = new StockReservationService(db);

        await service.ReserveStockAsync(
            orderId,
            [new OrderItemInfo { ProductId = product.Id, Quantity = 2 }],
            CancellationToken.None);
        await db.SaveChangesAsync();

        var releasedCount = await service.ReleaseReservedStockAsync(
            orderId,
            [new OrderItemInfo { ProductId = product.Id, Quantity = 2 }],
            "Payment cancelled",
            CancellationToken.None);
        await db.SaveChangesAsync();

        var duplicateReleaseCount = await service.ReleaseReservedStockAsync(
            orderId,
            [new OrderItemInfo { ProductId = product.Id, Quantity = 2 }],
            "Payment cancelled",
            CancellationToken.None);

        releasedCount.ShouldBe(1);
        duplicateReleaseCount.ShouldBe(0);
        product.StockQuantity.ShouldBe(5);
        (await db.StockReservations.SingleAsync()).Status.ShouldBe(StockReservationStatus.Released);
    }

    [Fact]
    public async Task ReserveStockAfterReleasedReservationIsIgnored()
    {
        await using var db = CreateInMemoryDbContext();
        var product = await SeedProductAsync(db, stockQuantity: 5, price: 89);
        var orderId = Guid.NewGuid();
        var service = new StockReservationService(db);

        await service.ReserveStockAsync(
            orderId,
            [new OrderItemInfo { ProductId = product.Id, Quantity = 2 }],
            CancellationToken.None);
        await db.SaveChangesAsync();

        await service.ReleaseReservedStockAsync(
            orderId,
            [new OrderItemInfo { ProductId = product.Id, Quantity = 2 }],
            "Order cancelled",
            CancellationToken.None);
        await db.SaveChangesAsync();

        var result = await service.ReserveStockAsync(
            orderId,
            [new OrderItemInfo { ProductId = product.Id, Quantity = 2 }],
            CancellationToken.None);

        result.Status.ShouldBe(StockReservationResultStatus.Ignored);
        product.StockQuantity.ShouldBe(5);
    }

    private static ProductDbContext CreateInMemoryDbContext()
    {
        var options = new DbContextOptionsBuilder<ProductDbContext>()
            .UseInMemoryDatabase($"product-stock-tests-{Guid.NewGuid():N}")
            .Options;

        return new ProductDbContext(options);
    }

    private static async Task<Product.Domain.Entities.Product> SeedProductAsync(
        ProductDbContext db,
        int stockQuantity,
        decimal price)
    {
        var category = Category.Create($"Outerwear-{Guid.NewGuid():N}");
        db.Categories.Add(category);
        await db.SaveChangesAsync();

        var product = Product.Domain.Entities.Product.Create(
            "Trail Jacket",
            price,
            stockQuantity,
            category.Id,
            "Shell",
            "https://example.com/trail.jpg");

        db.Products.Add(product);
        await db.SaveChangesAsync();

        return product;
    }
}
