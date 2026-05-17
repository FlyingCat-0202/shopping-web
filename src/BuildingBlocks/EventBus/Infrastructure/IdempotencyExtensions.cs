using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using StackExchange.Redis;

namespace EventBus.Infrastructure;

public static class IdempotencyExtensions
{
    public static IServiceCollection AddRedisIdempotency(this IServiceCollection services, string connectionString)
    {
        // Đăng ký Redis Multiplexer là Singleton để tối ưu connection
        services.AddSingleton<IConnectionMultiplexer>(sp =>
            ConnectionMultiplexer.Connect(connectionString));

        return services;
    }
}
