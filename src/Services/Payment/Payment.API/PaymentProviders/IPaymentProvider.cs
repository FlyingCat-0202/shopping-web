using Payment.Domain.Entities;

namespace Payment.API.PaymentProviders;

public interface IPaymentProvider
{
    string Name { get; }
    string RouteName { get; }

    bool SupportsPaymentMethod(string paymentMethod);

    Task<PaymentCheckoutResult> CreateCheckoutAsync(
        PaymentTransaction payment,
        HttpRequest request,
        CancellationToken cancellationToken);

    Task<PaymentCheckoutPageResult> CreateCheckoutPageAsync(
        PaymentTransaction payment,
        string? token,
        CancellationToken cancellationToken);

    PaymentProviderResult CompleteCheckout(
        PaymentTransaction payment,
        PaymentProviderCompleteRequest request);
}

public record PaymentCheckoutResult(
    string Provider,
    string ProviderKey,
    Guid PaymentId,
    string CheckoutUrl,
    DateTime ExpiresAt);

public record PaymentCheckoutPageResult(
    bool IsAuthorized,
    string? Html);

public record PaymentProviderCompleteRequest(
    bool Success,
    string Token);

public record PaymentProviderResult(
    bool IsAuthorized,
    bool IsExpired,
    bool Success,
    string? ProviderTransactionId,
    string FailureReason);

public record PaymentProviderSummary(
    string Name,
    string ProviderKey);
