using EventBus.Infrastructure;
using StackExchange.Redis;

namespace Cart.API.Idempotency;

public class RedisCartIdempotencyService(IConnectionMultiplexer redis) : IIdempotencyService
{
    private static readonly TimeSpan RequestTtl = TimeSpan.FromHours(24);
    private readonly IDatabase _database = redis.GetDatabase();

    public async Task<bool> RequestExistsAsync(Guid requestId)
    {
        return await _database.KeyExistsAsync(RequestKey(requestId));
    }

    public async Task CreateRequestAsync(Guid requestId)
    {
        await _database.StringSetAsync(
            RequestKey(requestId),
            RedisValue.EmptyString,
            RequestTtl,
            When.NotExists);
    }

    private static RedisKey RequestKey(Guid requestId) => $"cart-idempotency:{requestId}";
}
