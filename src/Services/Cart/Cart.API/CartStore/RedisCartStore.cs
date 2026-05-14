using StackExchange.Redis;

namespace Cart.API.CartStore;

public class RedisCartStore(IConnectionMultiplexer redis) : ICartStore
{
    private static readonly TimeSpan CartTtl = TimeSpan.FromDays(30);
    private readonly IDatabase _database = redis.GetDatabase();

    public async Task<List<CartStoreItem>> GetItemsAsync(Guid customerId)
    {
        var entries = await _database.HashGetAllAsync(CartKey(customerId));
        await RefreshCartTtlAsync(customerId, entries.Select(e => e.Name.ToString()));

        return entries
            .Select(e => new CartStoreItem(Guid.Parse(e.Name.ToString()), (int)e.Value))
            .Where(i => i.Quantity > 0)
            .ToList();
    }

    public async Task<int> GetQuantityAsync(Guid customerId, Guid productId)
    {
        var quantity = await _database.HashGetAsync(CartKey(customerId), productId.ToString());
        return quantity.HasValue ? (int)quantity : 0;
    }

    public async Task<bool> ItemExistsAsync(Guid customerId, Guid productId)
    {
        return await _database.HashExistsAsync(CartKey(customerId), productId.ToString());
    }

    public async Task UpsertItemAsync(Guid customerId, Guid productId, int quantity)
    {
        await _database.HashSetAsync(CartKey(customerId), productId.ToString(), quantity);
        await RefreshCartTtlAsync(customerId, [productId.ToString()]);
    }

    public async Task<bool> RemoveItemAsync(Guid customerId, Guid productId)
    {
        var removed = await _database.HashDeleteAsync(CartKey(customerId), productId.ToString());

        if (await _database.HashLengthAsync(CartKey(customerId)) == 0)
            await _database.KeyDeleteAsync(CartKey(customerId));

        return removed;
    }

    public async Task<int> RemoveItemsAsync(Guid customerId, IEnumerable<Guid> productIds)
    {
        var removed = 0;
        foreach (var productId in productIds.Distinct())
        {
            if (await RemoveItemAsync(customerId, productId))
                removed++;
        }

        return removed;
    }

    public async Task ClearAsync(Guid customerId)
    {
        await _database.KeyDeleteAsync(CartKey(customerId));
    }

    private async Task RefreshCartTtlAsync(Guid customerId, IEnumerable<string> productIds)
    {
        await _database.KeyExpireAsync(CartKey(customerId), CartTtl);
    }

    private static RedisKey CartKey(Guid customerId) => $"cart:{customerId}";
}
