using EventBus.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace Service.IntegrationTests;

public sealed class IdempotencyRegistrationTests
{
    [Fact]
    public void AddRedisIdempotencyRegistersEndpointFilter()
    {
        var services = new ServiceCollection();

        services.AddRedisIdempotency();

        services.ShouldContain(descriptor =>
            descriptor.ServiceType == typeof(IdempotencyFilter) &&
            descriptor.Lifetime == ServiceLifetime.Scoped);
    }
}
