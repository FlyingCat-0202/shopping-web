using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace EventBus.Infrastructure;

public static class IdempotencyExtensions
{
    public static IServiceCollection AddRedisIdempotency(this IServiceCollection services)
    {
        services.TryAddScoped<IdempotencyFilter>();

        return services;
    }
}
