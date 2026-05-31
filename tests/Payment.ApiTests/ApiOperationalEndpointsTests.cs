using System.Net;
using Shouldly;
using Xunit;

namespace Payment.ApiTests;

public class ApiOperationalEndpointsTests
{
    [SkippableFact]
    public async Task HealthEndpointsAndSecurityHeadersAreAvailable()
    {
        Skip.IfNot(PaymentApiFactory.IsDockerAvailable(), "Docker is required for Payment API integration tests.");

        await using var factory = await PaymentApiFactory.StartAsync();
        var client = factory.CreateClient();

        var live = await client.GetAsync("/health/live");
        var ready = await client.GetAsync("/health/ready");

        live.StatusCode.ShouldBe(HttpStatusCode.OK);
        ready.StatusCode.ShouldBe(HttpStatusCode.OK);
        live.Headers.TryGetValues("X-Content-Type-Options", out var contentTypeOptions).ShouldBeTrue();
        contentTypeOptions.ShouldContain("nosniff");
        live.Headers.TryGetValues("X-Frame-Options", out var frameOptions).ShouldBeTrue();
        frameOptions.ShouldContain("DENY");
    }
}
