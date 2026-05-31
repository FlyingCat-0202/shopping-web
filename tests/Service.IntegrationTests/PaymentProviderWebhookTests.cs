using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Payment.API.PaymentProviders;
using Payment.Domain.Entities;
using Shouldly;
using Xunit;

namespace Service.IntegrationTests;

public class PaymentProviderWebhookTests
{
    [Fact]
    public async Task FakeWalletProviderAcceptsOnlySignedCheckoutWebhookTokens()
    {
        var provider = new MeiMeiPaymentProvider(
            new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Payment:WebhookSecret"] = "integration-test-secret"
                })
                .Build(),
            new TestWebHostEnvironment());

        var payment = PaymentTransaction.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            120_000m,
            provider.Name);

        var httpContext = new DefaultHttpContext();
        httpContext.Request.Scheme = "https";
        httpContext.Request.Host = new HostString("store.test");

        var checkout = await provider.CreateCheckoutAsync(payment, httpContext.Request, CancellationToken.None);
        var token = Microsoft.AspNetCore.WebUtilities.QueryHelpers
            .ParseQuery(new Uri(checkout.CheckoutUrl).Query)["token"]
            .ToString();

        var accepted = provider.CompleteCheckout(payment, new PaymentProviderCompleteRequest(true, token));
        var rejected = provider.CompleteCheckout(payment, new PaymentProviderCompleteRequest(true, "bad-token"));

        accepted.IsAuthorized.ShouldBeTrue();
        accepted.Success.ShouldBeTrue();
        accepted.ProviderTransactionId.ShouldBe($"meimei-{payment.Id:N}");
        rejected.IsAuthorized.ShouldBeFalse();
        rejected.Success.ShouldBeFalse();
    }

    private sealed class TestWebHostEnvironment : IWebHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Development";
        public string ApplicationName { get; set; } = "Service.IntegrationTests";
        public string WebRootPath { get; set; } = Directory.GetCurrentDirectory();
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string ContentRootPath { get; set; } = Path.Combine(Directory.GetCurrentDirectory(), "src", "Services", "Payment", "Payment.API");
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
