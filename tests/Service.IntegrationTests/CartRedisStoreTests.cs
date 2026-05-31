using Cart.API.CartStore;
using DotNet.Testcontainers.Builders;
using Shouldly;
using StackExchange.Redis;
using Xunit;

namespace Service.IntegrationTests;

public class CartRedisStoreTests
{
    [SkippableFact]
    public async Task RedisCartStorePersistsUpdatesAndRemovals()
    {
        Skip.IfNot(ServiceIntegrationTestEnvironment.IsDockerAvailable(), "Docker is required for Redis integration tests.");

        await using var redisContainer = new ContainerBuilder()
            .WithImage("redis:7-alpine")
            .WithPortBinding(6379, true)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilInternalTcpPortIsAvailable(6379))
            .Build();

        await redisContainer.StartAsync();

        var endpoint = $"{redisContainer.Hostname}:{redisContainer.GetMappedPublicPort(6379)}";
        await using var redis = await ConnectionMultiplexer.ConnectAsync(endpoint);
        var store = new RedisCartStore(redis);
        var customerId = Guid.NewGuid();
        var firstProductId = Guid.NewGuid();
        var secondProductId = Guid.NewGuid();

        await store.UpsertItemAsync(customerId, firstProductId, 2);
        await store.UpsertItemAsync(customerId, secondProductId, 1);

        var items = await store.GetItemsAsync(customerId);
        items.ShouldContain(i => i.ProductId == firstProductId && i.Quantity == 2);
        items.ShouldContain(i => i.ProductId == secondProductId && i.Quantity == 1);
        (await store.GetQuantityAsync(customerId, firstProductId)).ShouldBe(2);
        (await store.ItemExistsAsync(customerId, secondProductId)).ShouldBeTrue();

        (await store.RemoveItemAsync(customerId, firstProductId)).ShouldBeTrue();
        (await store.RemoveItemsAsync(customerId, [secondProductId])).ShouldBe(1);

        (await store.GetItemsAsync(customerId)).ShouldBeEmpty();
    }
}
