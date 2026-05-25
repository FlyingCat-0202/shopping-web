using Microsoft.Extensions.DependencyInjection;

namespace EventBus.Infrastructure;

public static class IdempotencyExtensions
{
    public static IServiceCollection AddRedisIdempotency(this IServiceCollection services)
        => services;
}
