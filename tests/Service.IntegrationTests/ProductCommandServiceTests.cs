using EventBus.Contracts;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Product.API.Dtos;
using Product.API.Products;
using Product.Domain.Entities;
using Product.Infrastructure.Data;
using Shouldly;
using Xunit;

namespace Service.IntegrationTests;

public sealed class ProductCommandServiceTests
{
    [Fact]
    public async Task CreateCategoryRejectsDuplicateNames()
    {
        await using var db = CreateInMemoryDbContext();
        db.Categories.Add(new Category { Name = "Outerwear" });
        await db.SaveChangesAsync();

        var service = new ProductAdminCommandService(db, new CapturingPublishEndpoint());

        var result = await service.CreateCategoryAsync(new CategoryRequest("outerwear", null), CancellationToken.None);

        result.Status.ShouldBe(ProductOperationStatus.Conflict);
    }

    [Fact]
    public async Task QueueCreateProductRejectsMissingCategory()
    {
        await using var db = CreateInMemoryDbContext();
        var publisher = new CapturingPublishEndpoint();
        var service = new ProductAdminCommandService(db, publisher);

        var result = await service.QueueCreateProductAsync(
            new ProductRequest("Trail Jacket", 89, 5, null, null, CategoryId: 404),
            CancellationToken.None);

        result.Status.ShouldBe(ProductOperationStatus.BadRequest);
        publisher.Messages.ShouldBeEmpty();
    }

    [Fact]
    public async Task QueueUpdateProductPublishesCommandWhenProductAndCategoryExist()
    {
        await using var db = CreateInMemoryDbContext();
        var category = new Category { Name = "Outerwear" };
        var productId = Guid.NewGuid();
        db.Categories.Add(category);
        db.Products.Add(new Product.Domain.Entities.Product
        {
            Id = productId,
            Name = "Trail Jacket",
            Price = 89,
            StockQuantity = 5,
            Category = category,
            IsActive = true
        });
        await db.SaveChangesAsync();

        var publisher = new CapturingPublishEndpoint();
        var service = new ProductAdminCommandService(db, publisher);

        var result = await service.QueueUpdateProductAsync(
            productId,
            new UpdateProductRequest(productId, "Updated Jacket", 99, 8, true, "New", null, category.Id),
            CancellationToken.None);

        result.Status.ShouldBe(ProductOperationStatus.Accepted);
        publisher.Messages.Single().ShouldBeOfType<UpdateProductRequest>();
    }

    [Fact]
    public async Task QueueUpdateProductRejectsMismatchedRouteAndBodyIds()
    {
        await using var db = CreateInMemoryDbContext();
        var category = new Category { Name = "Outerwear" };
        db.Categories.Add(category);
        await db.SaveChangesAsync();

        var publisher = new CapturingPublishEndpoint();
        var service = new ProductAdminCommandService(db, publisher);

        var result = await service.QueueUpdateProductAsync(
            Guid.NewGuid(),
            new UpdateProductRequest(Guid.NewGuid(), "Updated Jacket", 99, 8, true, "New", null, category.Id),
            CancellationToken.None);

        result.Status.ShouldBe(ProductOperationStatus.BadRequest);
        publisher.Messages.ShouldBeEmpty();
    }

    [Fact]
    public async Task MutationCreateProductPersistsProductAndPublishesCreatedEvent()
    {
        await using var db = CreateInMemoryDbContext();
        var category = new Category { Name = "Outerwear" };
        db.Categories.Add(category);
        await db.SaveChangesAsync();

        var publisher = new CapturingPublishEndpoint();
        var service = new ProductMutationService(db, publisher);

        await service.CreateProductAsync(
            new CreateProductRequest(" Trail Jacket ", 89, 5, category.Id, " Shell ", null, true),
            CancellationToken.None);

        var product = await db.Products.SingleAsync();
        product.Name.ShouldBe("Trail Jacket");
        product.Description.ShouldBe("Shell");
        publisher.Messages.Single().ShouldBeOfType<ProductCreatedEvent>();
    }

    [Fact]
    public async Task MutationDeleteProductSoftDeletesAndPublishesDeletedEvent()
    {
        await using var db = CreateInMemoryDbContext();
        var category = new Category { Name = "Outerwear" };
        var product = new Product.Domain.Entities.Product
        {
            Id = Guid.NewGuid(),
            Name = "Trail Jacket",
            Price = 89,
            StockQuantity = 5,
            Category = category,
            IsActive = true
        };
        db.Categories.Add(category);
        db.Products.Add(product);
        await db.SaveChangesAsync();

        var publisher = new CapturingPublishEndpoint();
        var service = new ProductMutationService(db, publisher);

        await service.DeleteProductAsync(new DeleteProductRequest(product.Id), CancellationToken.None);

        product.IsActive.ShouldBeFalse();
        publisher.Messages.Single().ShouldBeOfType<ProductDeletedEvent>();
    }

    private static ProductDbContext CreateInMemoryDbContext()
    {
        var options = new DbContextOptionsBuilder<ProductDbContext>()
            .UseInMemoryDatabase($"product-command-tests-{Guid.NewGuid():N}")
            .Options;

        return new ProductDbContext(options);
    }

    private sealed class CapturingPublishEndpoint : IPublishEndpoint
    {
        public List<object> Messages { get; } = [];

        public ConnectHandle ConnectPublishObserver(IPublishObserver observer)
            => throw new NotSupportedException();

        public Task Publish<T>(T message, CancellationToken cancellationToken = default)
            where T : class
        {
            Messages.Add(message);
            return Task.CompletedTask;
        }

        public Task Publish<T>(T message, IPipe<PublishContext<T>> publishPipe, CancellationToken cancellationToken = default)
            where T : class
            => Publish(message, cancellationToken);

        public Task Publish<T>(T message, IPipe<PublishContext> publishPipe, CancellationToken cancellationToken = default)
            where T : class
            => Publish(message, cancellationToken);

        public Task Publish(object message, CancellationToken cancellationToken = default)
        {
            Messages.Add(message);
            return Task.CompletedTask;
        }

        public Task Publish(object message, IPipe<PublishContext> publishPipe, CancellationToken cancellationToken = default)
            => Publish(message, cancellationToken);

        public Task Publish(object message, Type messageType, CancellationToken cancellationToken = default)
        {
            Messages.Add(message);
            return Task.CompletedTask;
        }

        public Task Publish(object message, Type messageType, IPipe<PublishContext> publishPipe, CancellationToken cancellationToken = default)
            => Publish(message, messageType, cancellationToken);

        public Task Publish<T>(object values, CancellationToken cancellationToken = default)
            where T : class
        {
            Messages.Add(values);
            return Task.CompletedTask;
        }

        public Task Publish<T>(object values, IPipe<PublishContext<T>> publishPipe, CancellationToken cancellationToken = default)
            where T : class
            => Publish<T>(values, cancellationToken);

        public Task Publish<T>(object values, IPipe<PublishContext> publishPipe, CancellationToken cancellationToken = default)
            where T : class
            => Publish<T>(values, cancellationToken);
    }
}
