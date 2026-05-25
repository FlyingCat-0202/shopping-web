using Payment.Domain.Entities;
using Payment.Domain.Enums;
using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Payment.API.PaymentProviders;

public abstract class FakeWalletPaymentProvider(
    IConfiguration configuration,
    IWebHostEnvironment environment) : IPaymentProvider
{
    private const string TemplateFileName = "FakeWalletCheckout.html";
    private static readonly TimeSpan CheckoutLifetime = TimeSpan.FromMinutes(10);

    public abstract string Name { get; }
    public abstract string RouteName { get; }
    protected abstract string AccentColor { get; }
    protected abstract string PageBackgroundColor { get; }
    protected abstract string BorderColor { get; }
    protected abstract string ShadowColor { get; }

    public bool SupportsPaymentMethod(string paymentMethod)
        => string.Equals(paymentMethod, Name, StringComparison.OrdinalIgnoreCase) ||
           string.Equals(paymentMethod, RouteName, StringComparison.OrdinalIgnoreCase);

    public Task<PaymentCheckoutResult> CreateCheckoutAsync(
        PaymentTransaction payment,
        HttpRequest request,
        CancellationToken cancellationToken)
    {
        var token = ComputeToken(payment.Id);
        var publicScheme = request.Headers["X-Forwarded-Proto"].FirstOrDefault() ?? request.Scheme;
        var publicHost = request.Headers["X-Forwarded-Host"].FirstOrDefault() ?? request.Host.Value;
        var checkoutUrl =
            $"{publicScheme}://{publicHost}/api/payment/providers/{RouteName}/checkout/{payment.Id}?token={Uri.EscapeDataString(token)}";

        return Task.FromResult(new PaymentCheckoutResult(
            Name,
            RouteName,
            payment.Id,
            checkoutUrl,
            GetExpiresAt(payment)));
    }

    public async Task<PaymentCheckoutPageResult> CreateCheckoutPageAsync(
        PaymentTransaction payment,
        string? token,
        CancellationToken cancellationToken)
    {
        if (!IsValidToken(payment.Id, token))
            return new PaymentCheckoutPageResult(false, null);

        var isPending = payment.Status == PaymentStatus.Pending && !IsExpired(payment);
        var status = payment.Status.ToString();
        var message = isPending
            ? $"Choose the result you want {Name} to send back to the Payment service."
            : $"This payment is already {status}.";

        var html = await RenderTemplate(
            payment,
            token!,
            status,
            message,
            isPending ? string.Empty : "disabled",
            cancellationToken);

        return new PaymentCheckoutPageResult(true, html);
    }

    public PaymentProviderResult CompleteCheckout(
        PaymentTransaction payment,
        PaymentProviderCompleteRequest request)
    {
        if (!IsValidToken(payment.Id, request.Token))
        {
            return new PaymentProviderResult(
                IsAuthorized: false,
                IsExpired: false,
                Success: false,
                ProviderTransactionId: null,
                FailureReason: $"{Name} checkout token không hợp lệ.");
        }

        if (IsExpired(payment))
        {
            return new PaymentProviderResult(
                IsAuthorized: true,
                IsExpired: true,
                Success: false,
                ProviderTransactionId: null,
                FailureReason: $"{Name} checkout expired.");
        }

        if (request.Success)
        {
            return new PaymentProviderResult(
                IsAuthorized: true,
                IsExpired: false,
                Success: true,
                ProviderTransactionId: $"{RouteName}-{payment.Id:N}",
                FailureReason: string.Empty);
        }

        return new PaymentProviderResult(
            IsAuthorized: true,
            IsExpired: false,
            Success: false,
            ProviderTransactionId: null,
            FailureReason: $"{Name} checkout failed.");
    }

    private async Task<string> RenderTemplate(
        PaymentTransaction payment,
        string token,
        string status,
        string message,
        string disabledAttribute,
        CancellationToken cancellationToken)
    {
        var templatePath = Path.Combine(environment.ContentRootPath, "Templates", TemplateFileName);
        var template = await File.ReadAllTextAsync(templatePath, cancellationToken);
        var amount = payment.Amount.ToString("0.00", CultureInfo.InvariantCulture);

        return template
            .Replace("{{PROVIDER_NAME}}", WebUtility.HtmlEncode(Name))
            .Replace("{{PROVIDER_MARK}}", WebUtility.HtmlEncode(Name[..1]))
            .Replace("{{PROVIDER_ROUTE_JSON}}", JsonSerializer.Serialize(RouteName))
            .Replace("{{ACCENT_COLOR}}", AccentColor)
            .Replace("{{PAGE_BACKGROUND_COLOR}}", PageBackgroundColor)
            .Replace("{{BORDER_COLOR}}", BorderColor)
            .Replace("{{SHADOW_COLOR}}", ShadowColor)
            .Replace("{{MESSAGE}}", WebUtility.HtmlEncode(message))
            .Replace("{{AMOUNT}}", WebUtility.HtmlEncode(amount))
            .Replace("{{PAYMENT_ID}}", WebUtility.HtmlEncode(payment.Id.ToString()))
            .Replace("{{ORDER_ID}}", WebUtility.HtmlEncode(payment.OrderId.ToString()))
            .Replace("{{STATUS}}", WebUtility.HtmlEncode(status))
            .Replace("{{DISABLED_ATTRIBUTE}}", disabledAttribute)
            .Replace("{{TOKEN_JSON}}", JsonSerializer.Serialize(token))
            .Replace("{{PAYMENT_ID_JSON}}", JsonSerializer.Serialize(payment.Id.ToString()));
    }

    private DateTime GetExpiresAt(PaymentTransaction payment)
        => payment.CreatedAt.Add(CheckoutLifetime);

    private bool IsExpired(PaymentTransaction payment)
        => DateTime.UtcNow > GetExpiresAt(payment);

    private bool IsValidToken(Guid paymentId, string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
            return false;

        var expectedToken = ComputeToken(paymentId);
        if (token.Length != expectedToken.Length)
            return false;

        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(token),
            Encoding.UTF8.GetBytes(expectedToken));
    }

    private string ComputeToken(Guid paymentId)
    {
        var secret = configuration["Payment:WebhookSecret"];
        if (string.IsNullOrWhiteSpace(secret))
            throw new InvalidOperationException("Payment webhook secret is not configured.");

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes($"{RouteName}:{paymentId:N}"));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
