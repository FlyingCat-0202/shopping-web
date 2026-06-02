using EventBus.Contracts;
using EventBus.Extensions;
using EventBus.Infrastructure;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Payment.API.Dtos;
using Payment.API.PaymentProviders;
using Payment.Domain.Entities;
using Payment.Domain.Enums;
using Payment.Infrastructure.Data;
using System.Security.Cryptography;
using System.Security.Claims;
using System.Text;
using System.Text.Json;

namespace Payment.API.Endpoints;

public static class PaymentEndpoints
{
    private const string PaymentSucceededRoutingKey = "payment-succeeded";
    private const string PaymentFailedRoutingKey = "payment-failed";
    private const string WebhookSignatureHeader = "x-payment-signature";
    private const string WebhookTimestampHeader = "x-payment-timestamp";
    private static readonly TimeSpan WebhookClockTolerance = TimeSpan.FromMinutes(5);
    private static readonly JsonSerializerOptions WebhookJsonOptions = new(JsonSerializerDefaults.Web);

    public static void MapPaymentEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/payment")
            .WithTags("Payments")
            .RequireAuthorization();
        
        group.MapGet("/order/{orderId:guid}", GetPaymentByOrderId)
            .WithName("GetPaymentByOrderId");

        group.MapPost("/orders/query", GetPaymentsByOrderIds)
            .WithName("GetPaymentsByOrderIds");

        group.MapGet("/providers", GetPaymentProviders)
            .WithName("GetPaymentProviders");

        group.MapPost("/{id:guid}/providers/{provider}/checkout", CreateProviderCheckout)
            .AddEndpointFilter<IdempotencyFilter>()
            .WithName("CreateProviderCheckout");

        var admin = group.MapGroup("/admin")
            .RequireAuthorization(EndpointHelpers.AdminOnly);

        admin.MapGet("/", GetPayments)
            .WithName("GetAdminPayments");

        admin.MapGet("/{id:guid}", GetPaymentById)
            .WithName("GetAdminPaymentById");

        admin.MapPost("/{id:guid}/mock-webhook", ConfirmPaymentMockWebhook)
            .AddEndpointFilter<IdempotencyFilter>()
            .WithName("ConfirmPaymentMockWebhook");

        app.MapPost("/api/payment/webhook", ConfirmPaymentWebhook)
            .WithTags("Payments")
            .AllowAnonymous()
            .WithName("ConfirmPaymentWebhook");

        app.MapGet("/api/payment/providers/{provider}/checkout/{id:guid}", ShowProviderCheckout)
            .WithTags("Payments")
            .AllowAnonymous()
            .WithName("ShowProviderCheckout");

        app.MapPost("/api/payment/providers/{provider}/checkout/{id:guid}/complete", CompleteProviderCheckout)
            .WithTags("Payments")
            .AllowAnonymous()
            .WithName("CompleteProviderCheckout");
    }

    private static async Task<IResult> GetPaymentByOrderId(
        Guid orderId,
        ClaimsPrincipal user,
        PaymentDbContext db,
        CancellationToken cancellationToken)
    {
        if (!EndpointHelpers.TryGetCustomerId(user, out var customerId))
            return Results.Unauthorized();

        var payment = await db.Payments.AsNoTracking()
            .Where(p => p.OrderId == orderId && p.CustomerId == customerId)
            .Select(p => new PaymentResponse(
                p.Id,
                p.OrderId,
                p.Amount,
                p.PaymentMethod,
                p.Status.ToString(),
                p.ProviderTransactionId,
                p.FailureReason,
                p.CreatedAt,
                p.CompletedAt))
            .FirstOrDefaultAsync(cancellationToken);

        return payment is null ? Results.NotFound() : Results.Ok(payment);
    }

    private static async Task<IResult> GetPaymentsByOrderIds(
        PaymentsByOrderIdsRequest request,
        ClaimsPrincipal user,
        PaymentDbContext db,
        CancellationToken cancellationToken)
    {
        if (!EndpointHelpers.TryGetCustomerId(user, out var customerId))
            return Results.Unauthorized();

        var orderIds = request.OrderIds
            .Where(id => id != Guid.Empty)
            .Distinct()
            .Take(50)
            .ToArray();

        if (orderIds.Length == 0)
            return Results.Ok(Array.Empty<PaymentResponse>());

        var payments = await db.Payments.AsNoTracking()
            .Where(p => p.CustomerId == customerId && orderIds.Contains(p.OrderId))
            .Select(p => new PaymentResponse(
                p.Id,
                p.OrderId,
                p.Amount,
                p.PaymentMethod,
                p.Status.ToString(),
                p.ProviderTransactionId,
                p.FailureReason,
                p.CreatedAt,
                p.CompletedAt))
            .ToListAsync(cancellationToken);

        return Results.Ok(payments);
    }

    private static IResult GetPaymentProviders(PaymentProviderCatalog providers)
        => Results.Ok(providers.GetAll());

    private static async Task<IResult> CreateProviderCheckout(
        Guid id,
        string provider,
        ClaimsPrincipal user,
        HttpRequest request,
        PaymentDbContext db,
        PaymentProviderCatalog providers,
        CancellationToken cancellationToken)
    {
        if (!EndpointHelpers.TryGetCustomerId(user, out var customerId))
            return Results.Unauthorized();

        var paymentProvider = providers.FindByRoute(provider);
        if (paymentProvider is null)
            return Results.BadRequest(new { message = $"Provider {provider} không được hỗ trợ." });

        var payment = await db.Payments.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

        if (payment is null)
            return Results.NotFound();

        if (payment.CustomerId != customerId)
            return Results.Forbid();

        if (payment.Status != PaymentStatus.Pending)
            return Results.Conflict(new { message = $"Payment đang ở trạng thái {payment.Status}." });

        if (!paymentProvider.SupportsPaymentMethod(payment.PaymentMethod))
        {
            return Results.BadRequest(new
            {
                message = $"Payment method {payment.PaymentMethod} không thể thanh toán bằng {paymentProvider.Name}."
            });
        }

        var checkout = await paymentProvider.CreateCheckoutAsync(payment, request, cancellationToken);
        return Results.Ok(checkout);
    }

    private static async Task<IResult> ShowProviderCheckout(
        Guid id,
        string provider,
        string? token,
        PaymentDbContext db,
        PaymentProviderCatalog providers,
        CancellationToken cancellationToken)
    {
        var paymentProvider = providers.FindByRoute(provider);
        if (paymentProvider is null)
            return Results.NotFound();

        var payment = await db.Payments.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

        if (payment is null)
            return Results.NotFound();

        var page = await paymentProvider.CreateCheckoutPageAsync(payment, token, cancellationToken);
        if (!page.IsAuthorized)
            return Results.Unauthorized();

        return Results.Content(page.Html!, "text/html; charset=utf-8");
    }

    private static async Task<IResult> CompleteProviderCheckout(
        Guid id,
        string provider,
        PaymentProviderCompleteRequest request,
        PaymentDbContext db,
        IPublishEndpoint publishEndpoint,
        PaymentProviderCatalog providers,
        CancellationToken cancellationToken)
    {
        var paymentProvider = providers.FindByRoute(provider);
        if (paymentProvider is null)
            return Results.NotFound();

        var payment = await db.Payments
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

        if (payment is null)
            return Results.NotFound();

        if (IsTerminal(payment))
            return Results.Ok(ToPaymentResponse(payment));

        var providerResult = paymentProvider.CompleteCheckout(payment, request);
        if (!providerResult.IsAuthorized)
            return Results.Unauthorized();

        if (providerResult.IsExpired)
        {
            payment.MarkExpired(providerResult.FailureReason);
            await PublishFailed(publishEndpoint, payment, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            return Results.Conflict(new { message = $"{paymentProvider.Name} checkout đã hết hạn." });
        }

        if (providerResult.Success)
        {
            payment.MarkSucceeded(providerResult.ProviderTransactionId ?? $"{paymentProvider.RouteName}-{payment.Id:N}");
            await PublishSucceeded(publishEndpoint, payment, cancellationToken);
        }
        else
        {
            payment.MarkFailed(providerResult.FailureReason);
            await PublishFailed(publishEndpoint, payment, cancellationToken);
        }

        await db.SaveChangesAsync(cancellationToken);

        return Results.Ok(ToPaymentResponse(payment));
    }

    private static async Task<IResult> ConfirmPaymentWebhook(
        HttpRequest request,
        PaymentDbContext db,
        IPublishEndpoint publishEndpoint,
        IConfiguration configuration,
        CancellationToken cancellationToken)
    {
        var secret = configuration["Payment:WebhookSecret"];
        if (string.IsNullOrWhiteSpace(secret))
            return Results.Problem("Payment webhook secret is not configured.");

        using var reader = new StreamReader(request.Body, Encoding.UTF8);
        var payload = await reader.ReadToEndAsync(cancellationToken);

        if (!IsValidWebhookSignature(request, payload, secret))
            return Results.Unauthorized();

        var webhook = JsonSerializer.Deserialize<PaymentWebhookRequest>(payload, WebhookJsonOptions);

        if (webhook is null)
            return Results.BadRequest(new { message = "Webhook body không hợp lệ." });

        if (webhook.PaymentId == Guid.Empty)
            return Results.BadRequest(new { message = "PaymentId không hợp lệ." });

        var payment = await db.Payments
            .FirstOrDefaultAsync(p => p.Id == webhook.PaymentId, cancellationToken);

        if (payment is null)
            return Results.NotFound();

        if (IsTerminal(payment))
            return await RepublishTerminalPayment(payment, db, publishEndpoint, cancellationToken);

        await ApplyPaymentResult(
            payment,
            webhook.Success,
            webhook.ProviderTransactionId,
            webhook.Reason ?? "Cổng thanh toán báo thất bại.",
            $"webhook-{payment.Id:N}",
            publishEndpoint,
            cancellationToken);

        await db.SaveChangesAsync(cancellationToken);

        return Results.Ok(ToPaymentResponse(payment));
    }

    private static async Task<IResult> GetPayments(
        PaymentDbContext db,
        CancellationToken cancellationToken,
        int pageIndex = 0,
        int pageSize = 20,
        Guid? orderId = null,
        Guid? customerId = null,
        string? status = null)
    {
        pageSize = Math.Clamp(pageSize, 1, 50);
        pageIndex = Math.Max(pageIndex, 0);

        var query = db.Payments.AsNoTracking();

        if (orderId.HasValue)
            query = query.Where(p => p.OrderId == orderId.Value);

        if (customerId.HasValue)
            query = query.Where(p => p.CustomerId == customerId.Value);

        if (!string.IsNullOrWhiteSpace(status))
        {
            if (!Enum.TryParse<PaymentStatus>(status, true, out var paymentStatus))
                return Results.BadRequest(new { message = "Trạng thái payment không hợp lệ." });

            query = query.Where(p => p.Status == paymentStatus);
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var payments = await query
            .OrderByDescending(p => p.CreatedAt)
            .Skip(pageIndex * pageSize)
            .Take(pageSize)
            .Select(p => new AdminPaymentResponse(
                p.Id,
                p.OrderId,
                p.CustomerId,
                p.Amount,
                p.PaymentMethod,
                p.Status.ToString(),
                p.ProviderTransactionId,
                p.FailureReason,
                p.CreatedAt,
                p.CompletedAt))
            .ToListAsync(cancellationToken);

        return Results.Ok(new PaymentPagedResult<AdminPaymentResponse>(payments, totalCount, pageIndex, pageSize));
    }

    private static async Task<IResult> ConfirmPaymentMockWebhook(
        Guid id,
        PaymentMockWebhookRequest request,
        PaymentDbContext db,
        IPublishEndpoint publishEndpoint,
        CancellationToken cancellationToken)
    {
        var payment = await db.Payments
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

        if (payment is null)
            return Results.NotFound();

        if (IsTerminal(payment))
            return await RepublishTerminalPayment(payment, db, publishEndpoint, cancellationToken);

        await ApplyPaymentResult(
            payment,
            request.Success,
            request.ProviderTransactionId,
            request.Reason ?? "Admin mock payment failure.",
            $"mock-webhook-{payment.Id:N}",
            publishEndpoint,
            cancellationToken);

        await db.SaveChangesAsync(cancellationToken);

        return Results.Ok(ToPaymentResponse(payment));
    }

    private static async Task<IResult> GetPaymentById(
        Guid id,
        PaymentDbContext db,
        CancellationToken cancellationToken)
    {
        var payment = await db.Payments.AsNoTracking()
            .Where(p => p.Id == id)
            .Select(p => new AdminPaymentResponse(
                p.Id,
                p.OrderId,
                p.CustomerId,
                p.Amount,
                p.PaymentMethod,
                p.Status.ToString(),
                p.ProviderTransactionId,
                p.FailureReason,
                p.CreatedAt,
                p.CompletedAt))
            .FirstOrDefaultAsync(cancellationToken);

        return payment is null ? Results.NotFound() : Results.Ok(payment);
    }

    private static PaymentResponse ToPaymentResponse(PaymentTransaction payment)
        => new(
            payment.Id,
            payment.OrderId,
            payment.Amount,
            payment.PaymentMethod,
            payment.Status.ToString(),
            payment.ProviderTransactionId,
            payment.FailureReason,
            payment.CreatedAt,
            payment.CompletedAt);

    private static bool IsTerminal(PaymentTransaction payment)
        => payment.Status is PaymentStatus.Succeeded or PaymentStatus.Failed or PaymentStatus.Expired or PaymentStatus.Refunded;

    private static async Task<IResult> RepublishTerminalPayment(
        PaymentTransaction payment,
        PaymentDbContext db,
        IPublishEndpoint publishEndpoint,
        CancellationToken cancellationToken)
    {
        await PublishCurrentPaymentState(publishEndpoint, payment, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return Results.Ok(ToPaymentResponse(payment));
    }

    private static async Task ApplyPaymentResult(
        PaymentTransaction payment,
        bool success,
        string? providerTransactionId,
        string failureReason,
        string defaultProviderTransactionId,
        IPublishEndpoint publishEndpoint,
        CancellationToken cancellationToken)
    {
        if (success)
        {
            payment.MarkSucceeded(NormalizeProviderTransactionId(providerTransactionId, defaultProviderTransactionId));
            await PublishSucceeded(publishEndpoint, payment, cancellationToken);
            return;
        }

        payment.MarkFailed(failureReason);
        await PublishFailed(publishEndpoint, payment, cancellationToken);
    }

    private static string NormalizeProviderTransactionId(string? value, string fallback)
        => string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();

    private static Task PublishCurrentPaymentState(
        IPublishEndpoint publishEndpoint,
        PaymentTransaction payment,
        CancellationToken cancellationToken)
        => payment.Status switch
        {
            PaymentStatus.Succeeded => PublishSucceeded(publishEndpoint, payment, cancellationToken),
            PaymentStatus.Refunded => PublishRefunded(publishEndpoint, payment, cancellationToken),
            _ => PublishFailed(publishEndpoint, payment, cancellationToken)
        };

    private static Task PublishSucceeded(
        IPublishEndpoint publishEndpoint,
        PaymentTransaction payment,
        CancellationToken cancellationToken)
        => publishEndpoint.Publish(new PaymentSucceededEvent
        {
            CorrelationId = payment.OrderId,
            PaymentId = payment.Id,
            OrderId = payment.OrderId,
            CustomerId = payment.CustomerId,
            Amount = payment.Amount,
            PaymentMethod = payment.PaymentMethod,
            ProviderTransactionId = payment.ProviderTransactionId ?? string.Empty
        }, ctx => ctx.SetRoutingKey(PaymentSucceededRoutingKey), cancellationToken);

    private static Task PublishFailed(
        IPublishEndpoint publishEndpoint,
        PaymentTransaction payment,
        CancellationToken cancellationToken)
        => publishEndpoint.Publish(new PaymentFailedEvent
        {
            CorrelationId = payment.OrderId,
            PaymentId = payment.Id,
            OrderId = payment.OrderId,
            CustomerId = payment.CustomerId,
            Reason = payment.FailureReason ?? "Thanh toán thất bại."
        }, ctx => ctx.SetRoutingKey(PaymentFailedRoutingKey), cancellationToken);

    private static Task PublishRefunded(
        IPublishEndpoint publishEndpoint,
        PaymentTransaction payment,
        CancellationToken cancellationToken)
        => publishEndpoint.Publish(new PaymentRefundedEvent
        {
            CorrelationId = payment.OrderId,
            PaymentId = payment.Id,
            OrderId = payment.OrderId,
            CustomerId = payment.CustomerId,
            Amount = payment.Amount,
            PaymentMethod = payment.PaymentMethod,
            ProviderTransactionId = payment.ProviderTransactionId ?? string.Empty,
            Reason = payment.FailureReason ?? "Payment was refunded."
        }, cancellationToken);

    private static bool IsValidWebhookSignature(HttpRequest request, string payload, string secret)
    {
        var timestampValue = request.Headers[WebhookTimestampHeader].FirstOrDefault();
        var signatureValue = request.Headers[WebhookSignatureHeader].FirstOrDefault();

        if (string.IsNullOrWhiteSpace(timestampValue) || string.IsNullOrWhiteSpace(signatureValue))
            return false;

        if (!long.TryParse(timestampValue, out var unixSeconds))
            return false;

        var timestamp = DateTimeOffset.FromUnixTimeSeconds(unixSeconds);
        if (DateTimeOffset.UtcNow - timestamp > WebhookClockTolerance ||
            timestamp - DateTimeOffset.UtcNow > WebhookClockTolerance)
            return false;

        var expectedSignature = ComputeSignature(timestampValue, payload, secret);
        var providedSignature = signatureValue.StartsWith("sha256=", StringComparison.OrdinalIgnoreCase)
            ? signatureValue["sha256=".Length..]
            : signatureValue;

        if (providedSignature.Length != expectedSignature.Length)
            return false;

        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(providedSignature),
            Encoding.UTF8.GetBytes(expectedSignature));
    }

    private static string ComputeSignature(string timestamp, string payload, string secret)
        => ComputeHmac($"{timestamp}.{payload}", secret);

    private static string ComputeHmac(string value, string secret)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
