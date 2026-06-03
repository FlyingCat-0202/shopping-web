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
        var ready = await WaitForReadyAsync(client);

        live.StatusCode.ShouldBe(HttpStatusCode.OK);
        ready.StatusCode.ShouldBe(HttpStatusCode.OK);
        live.Headers.TryGetValues("X-Content-Type-Options", out var contentTypeOptions).ShouldBeTrue();
        contentTypeOptions.ShouldContain("nosniff");
        live.Headers.TryGetValues("X-Frame-Options", out var frameOptions).ShouldBeTrue();
        frameOptions.ShouldContain("DENY");
    }

    private static async Task<HttpResponseMessage> WaitForReadyAsync(HttpClient client)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(60);
        HttpResponseMessage? lastResponse = null;
        var lastBody = string.Empty;

        while (DateTimeOffset.UtcNow < deadline)
        {
            lastResponse?.Dispose();
            lastResponse = await client.GetAsync("/health/ready");

            if (lastResponse.StatusCode == HttpStatusCode.OK)
                return lastResponse;

            lastBody = await lastResponse.Content.ReadAsStringAsync();
            await Task.Delay(TimeSpan.FromSeconds(1));
        }

        lastResponse.ShouldNotBeNull($"Payment API did not return a readiness response. Last body: {lastBody}");
        return lastResponse!;
    }
}
