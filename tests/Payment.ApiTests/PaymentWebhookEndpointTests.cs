using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Payment.API.Dtos;
using Payment.API.PaymentProviders;
using Payment.Domain.Entities;
using Payment.Domain.Enums;
using Shouldly;
using Xunit;

namespace Payment.ApiTests;

public class PaymentWebhookEndpointTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [SkippableFact]
    public async Task SignedWebhookMarksPaymentSucceeded()
    {
        Skip.IfNot(PaymentApiFactory.IsDockerAvailable(), "Docker is required for Payment API integration tests.");

        await using var factory = await PaymentApiFactory.StartAsync();
        var client = factory.CreateClient();
        var payment = PaymentTransaction.Create(Guid.NewGuid(), Guid.NewGuid(), 125_000m, "MeiMei");
        await factory.SeedAsync(db => db.Payments.Add(payment));

        var payload = JsonSerializer.Serialize(
            new PaymentWebhookRequest(payment.Id, true, "provider-transaction-1", null),
            JsonOptions);
        using var request = SignedWebhookRequest(payload);

        var response = await client.SendAsync(request);
        var body = await response.Content.ReadFromJsonAsync<PaymentResponse>(JsonOptions);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        body.ShouldNotBeNull();
        body!.Status.ShouldBe(PaymentStatus.Succeeded.ToString());
        body.ProviderTransactionId.ShouldBe("provider-transaction-1");

        var storedPayment = await factory.QueryAsync(db =>
            db.Payments.AsNoTracking().SingleAsync(p => p.Id == payment.Id));

        storedPayment.Status.ShouldBe(PaymentStatus.Succeeded);
        storedPayment.ProviderTransactionId.ShouldBe("provider-transaction-1");
    }

    [SkippableFact]
    public async Task WebhookRejectsInvalidSignatureWithoutMutatingPayment()
    {
        Skip.IfNot(PaymentApiFactory.IsDockerAvailable(), "Docker is required for Payment API integration tests.");

        await using var factory = await PaymentApiFactory.StartAsync();
        var client = factory.CreateClient();
        var payment = PaymentTransaction.Create(Guid.NewGuid(), Guid.NewGuid(), 90_000m, "MeilyMeily");
        await factory.SeedAsync(db => db.Payments.Add(payment));

        var payload = JsonSerializer.Serialize(
            new PaymentWebhookRequest(payment.Id, true, "provider-transaction-2", null),
            JsonOptions);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/payment/webhook")
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };
        request.Headers.Add("x-payment-timestamp", DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString());
        request.Headers.Add("x-payment-signature", "sha256=invalid");

        var response = await client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        var storedPayment = await factory.QueryAsync(db =>
            db.Payments.AsNoTracking().SingleAsync(p => p.Id == payment.Id));

        storedPayment.Status.ShouldBe(PaymentStatus.Pending);
        storedPayment.ProviderTransactionId.ShouldBeNull();
    }

    [SkippableFact]
    public async Task ProviderCheckoutCompleteEndpointMarksPaymentFailed()
    {
        Skip.IfNot(PaymentApiFactory.IsDockerAvailable(), "Docker is required for Payment API integration tests.");

        await using var factory = await PaymentApiFactory.StartAsync();
        var client = factory.CreateClient();
        var payment = PaymentTransaction.Create(Guid.NewGuid(), Guid.NewGuid(), 75_000m, "MeiMei");
        await factory.SeedAsync(db => db.Payments.Add(payment));

        var token = ComputeCheckoutToken("meimei", payment.Id);
        var response = await client.PostAsJsonAsync(
            $"/api/payment/providers/meimei/checkout/{payment.Id}/complete",
            new PaymentProviderCompleteRequest(false, token),
            JsonOptions);
        var body = await response.Content.ReadFromJsonAsync<PaymentResponse>(JsonOptions);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        body.ShouldNotBeNull();
        body!.Status.ShouldBe(PaymentStatus.Failed.ToString());
        body.FailureReason.ShouldBe("MeiMei checkout failed.");
    }

    private static HttpRequestMessage SignedWebhookRequest(string payload)
    {
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        var signature = ComputeWebhookSignature(timestamp, payload);
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/payment/webhook")
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };

        request.Headers.Add("x-payment-timestamp", timestamp);
        request.Headers.Add("x-payment-signature", $"sha256={signature}");

        return request;
    }

    private static string ComputeWebhookSignature(string timestamp, string payload)
        => ComputeHmac($"{timestamp}.{payload}", PaymentApiFactory.WebhookSecret);

    private static string ComputeCheckoutToken(string provider, Guid paymentId)
        => ComputeHmac($"{provider}:{paymentId:N}", PaymentApiFactory.WebhookSecret);

    private static string ComputeHmac(string value, string secret)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
