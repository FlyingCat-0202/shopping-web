using EventBus.Contracts;
using EventBus.Extensions;
using EventBus.Infrastructure;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Payment.Domain.Entities;
using Payment.Domain.Enums;
using Payment.Infrastructure.Data;
using System.Globalization;
using System.Net;
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

        group.MapPost("/{id:guid}/confirm", ConfirmPayment)
            .AddEndpointFilter<IdempotencyFilter>()
            .WithName("ConfirmPayment");

        group.MapPost("/{id:guid}/meimei/checkout", CreateMeiMeiCheckout)
            .AddEndpointFilter<IdempotencyFilter>()
            .WithName("CreateMeiMeiCheckout");

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

        app.MapGet("/api/payment/meimei/checkout/{id:guid}", ShowMeiMeiCheckout)
            .WithTags("Payments")
            .AllowAnonymous()
            .WithName("ShowMeiMeiCheckout");

        app.MapPost("/api/payment/meimei/checkout/{id:guid}/complete", CompleteMeiMeiCheckout)
            .WithTags("Payments")
            .AllowAnonymous()
            .WithName("CompleteMeiMeiCheckout");
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

    private static async Task<IResult> ConfirmPayment(
        Guid id,
        ClaimsPrincipal user,
        PaymentDbContext db,
        IPublishEndpoint publishEndpoint,
        CancellationToken cancellationToken)
    {
        if (!EndpointHelpers.TryGetCustomerId(user, out var customerId))
            return Results.Unauthorized();

        var payment = await db.Payments
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

        if (payment is null)
            return Results.NotFound();

        if (payment.CustomerId != customerId)
            return Results.Forbid();

        if (payment.Status == PaymentStatus.Succeeded)
        {
            await PublishSucceeded(publishEndpoint, payment, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            return Results.Ok(ToPaymentResponse(payment));
        }

        if (payment.Status != PaymentStatus.Pending)
            return Results.Conflict(new { message = $"Payment đang ở trạng thái {payment.Status}." });

        payment.MarkSucceeded($"manual-{payment.Id:N}");
        await PublishSucceeded(publishEndpoint, payment, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);

        return Results.Ok(ToPaymentResponse(payment));
    }

    private static async Task<IResult> CreateMeiMeiCheckout(
        Guid id,
        ClaimsPrincipal user,
        HttpRequest request,
        PaymentDbContext db,
        IConfiguration configuration,
        CancellationToken cancellationToken)
    {
        if (!EndpointHelpers.TryGetCustomerId(user, out var customerId))
            return Results.Unauthorized();

        var secret = configuration["Payment:WebhookSecret"];
        if (string.IsNullOrWhiteSpace(secret))
            return Results.Problem("Payment webhook secret is not configured.");

        var payment = await db.Payments.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

        if (payment is null)
            return Results.NotFound();

        if (payment.CustomerId != customerId)
            return Results.Forbid();

        if (payment.Status != PaymentStatus.Pending)
            return Results.Conflict(new { message = $"Payment đang ở trạng thái {payment.Status}." });

        var token = ComputeMeiMeiToken(payment.Id, secret);
        var checkoutUrl = $"{request.Scheme}://{request.Host}/api/payment/meimei/checkout/{payment.Id}?token={Uri.EscapeDataString(token)}";

        return Results.Ok(new MeiMeiCheckoutResponse(
            "MeiMei",
            payment.Id,
            checkoutUrl,
            GetMeiMeiExpiresAt(payment)));
    }

    private static async Task<IResult> ShowMeiMeiCheckout(
        Guid id,
        string? token,
        PaymentDbContext db,
        IConfiguration configuration,
        IWebHostEnvironment environment,
        CancellationToken cancellationToken)
    {
        var secret = configuration["Payment:WebhookSecret"];
        if (string.IsNullOrWhiteSpace(secret))
            return Results.Problem("Payment webhook secret is not configured.");

        if (!IsValidMeiMeiToken(id, token, secret))
            return Results.Unauthorized();

        var payment = await db.Payments.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

        if (payment is null)
            return Results.NotFound();

        var isPending = payment.Status == PaymentStatus.Pending && !IsMeiMeiExpired(payment);
        var amount = payment.Amount.ToString("0.00", CultureInfo.InvariantCulture);
        var status = payment.Status.ToString();
        var disabledAttribute = isPending ? string.Empty : "disabled";
        var message = isPending
            ? "Choose the result you want MeiMei to send back to the Payment service."
            : $"This payment is already {status}.";

        var html = await RenderMeiMeiCheckoutTemplate(
            payment,
            token!,
            amount,
            status,
            message,
            disabledAttribute,
            environment,
            cancellationToken);

        return Results.Content(html, "text/html; charset=utf-8");
    }

    private static async Task<IResult> CompleteMeiMeiCheckout(
        Guid id,
        MeiMeiCompleteRequest request,
        PaymentDbContext db,
        IPublishEndpoint publishEndpoint,
        IConfiguration configuration,
        CancellationToken cancellationToken)
    {
        var secret = configuration["Payment:WebhookSecret"];
        if (string.IsNullOrWhiteSpace(secret))
            return Results.Problem("Payment webhook secret is not configured.");

        if (!IsValidMeiMeiToken(id, request.Token, secret))
            return Results.Unauthorized();

        var payment = await db.Payments
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

        if (payment is null)
            return Results.NotFound();

        if (payment.Status == PaymentStatus.Succeeded)
            return Results.Ok(ToPaymentResponse(payment));

        if (payment.Status is PaymentStatus.Failed or PaymentStatus.Expired)
            return Results.Ok(ToPaymentResponse(payment));

        if (IsMeiMeiExpired(payment))
        {
            payment.MarkExpired("MeiMei checkout expired.");
            await PublishFailed(publishEndpoint, payment, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            return Results.Conflict(new { message = "MeiMei checkout đã hết hạn." });
        }

        if (request.Success)
        {
            payment.MarkSucceeded($"meimei-{payment.Id:N}");
            await PublishSucceeded(publishEndpoint, payment, cancellationToken);
        }
        else
        {
            payment.MarkFailed("MeiMei checkout failed.");
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

        if (payment.Status == PaymentStatus.Succeeded)
        {
            await PublishSucceeded(publishEndpoint, payment, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            return Results.Ok(ToPaymentResponse(payment));
        }

        if (payment.Status is PaymentStatus.Failed or PaymentStatus.Expired)
        {
            await PublishFailed(publishEndpoint, payment, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            return Results.Ok(ToPaymentResponse(payment));
        }

        if (webhook.Success)
        {
            payment.MarkSucceeded(
                string.IsNullOrWhiteSpace(webhook.ProviderTransactionId)
                    ? $"webhook-{payment.Id:N}"
                    : webhook.ProviderTransactionId.Trim());

            await PublishSucceeded(publishEndpoint, payment, cancellationToken);
        }
        else
        {
            payment.MarkFailed(webhook.Reason ?? "Cổng thanh toán báo thất bại.");
            await PublishFailed(publishEndpoint, payment, cancellationToken);
        }

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

        if (payment.Status == PaymentStatus.Succeeded)
        {
            await PublishSucceeded(publishEndpoint, payment, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            return Results.Ok(ToPaymentResponse(payment));
        }

        if (payment.Status is PaymentStatus.Failed or PaymentStatus.Expired)
        {
            await PublishFailed(publishEndpoint, payment, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            return Results.Ok(ToPaymentResponse(payment));
        }

        if (request.Success)
        {
            payment.MarkSucceeded(
                string.IsNullOrWhiteSpace(request.ProviderTransactionId)
                    ? $"mock-webhook-{payment.Id:N}"
                    : request.ProviderTransactionId.Trim());

            await PublishSucceeded(publishEndpoint, payment, cancellationToken);
        }
        else
        {
            payment.MarkFailed(request.Reason ?? "Admin mock payment failure.");
            await PublishFailed(publishEndpoint, payment, cancellationToken);
        }

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

    private static Task PublishSucceeded(
        IPublishEndpoint publishEndpoint,
        PaymentTransaction payment,
        CancellationToken cancellationToken)
        => publishEndpoint.Publish(new PaymentSucceededEvent
        {
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
            PaymentId = payment.Id,
            OrderId = payment.OrderId,
            CustomerId = payment.CustomerId,
            Reason = payment.FailureReason ?? "Thanh toán thất bại."
        }, ctx => ctx.SetRoutingKey(PaymentFailedRoutingKey), cancellationToken);

    private static async Task<string> RenderMeiMeiCheckoutTemplate(
        PaymentTransaction payment,
        string token,
        string amount,
        string status,
        string message,
        string disabledAttribute,
        IWebHostEnvironment environment,
        CancellationToken cancellationToken)
    {
        var templatePath = Path.Combine(environment.ContentRootPath, "Templates", "MeiMeiCheckout.html");
        var template = await File.ReadAllTextAsync(templatePath, cancellationToken);

        return template
            .Replace("{{MESSAGE}}", WebUtility.HtmlEncode(message))
            .Replace("{{AMOUNT}}", WebUtility.HtmlEncode(amount))
            .Replace("{{PAYMENT_ID}}", WebUtility.HtmlEncode(payment.Id.ToString()))
            .Replace("{{ORDER_ID}}", WebUtility.HtmlEncode(payment.OrderId.ToString()))
            .Replace("{{STATUS}}", WebUtility.HtmlEncode(status))
            .Replace("{{DISABLED_ATTRIBUTE}}", disabledAttribute)
            .Replace("{{TOKEN_JSON}}", JsonSerializer.Serialize(token))
            .Replace("{{PAYMENT_ID_JSON}}", JsonSerializer.Serialize(payment.Id.ToString()));
    }

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

    private static string ComputeMeiMeiToken(Guid paymentId, string secret)
        => ComputeHmac($"meimei:{paymentId:N}", secret);

    private static bool IsValidMeiMeiToken(Guid paymentId, string? token, string secret)
    {
        if (string.IsNullOrWhiteSpace(token))
            return false;

        var expectedToken = ComputeMeiMeiToken(paymentId, secret);

        if (token.Length != expectedToken.Length)
            return false;

        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(token),
            Encoding.UTF8.GetBytes(expectedToken));
    }

    private static DateTime GetMeiMeiExpiresAt(PaymentTransaction payment)
        => payment.CreatedAt.AddMinutes(10);

    private static bool IsMeiMeiExpired(PaymentTransaction payment)
        => DateTime.UtcNow > GetMeiMeiExpiresAt(payment);

    private static string ComputeHmac(string value, string secret)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}

public record PaymentWebhookRequest(
    Guid PaymentId,
    bool Success,
    string? ProviderTransactionId,
    string? Reason);

public record PaymentMockWebhookRequest(
    bool Success,
    string? ProviderTransactionId,
    string? Reason);

public record MeiMeiCheckoutResponse(
    string Provider,
    Guid PaymentId,
    string CheckoutUrl,
    DateTime ExpiresAt);

public record MeiMeiCompleteRequest(
    bool Success,
    string Token);

public record PaymentPagedResult<T>(
    IReadOnlyList<T> Items,
    int TotalCount,
    int PageIndex,
    int PageSize);

public record PaymentResponse(
    Guid Id,
    Guid OrderId,
    decimal Amount,
    string PaymentMethod,
    string Status,
    string? ProviderTransactionId,
    string? FailureReason,
    DateTime CreatedAt,
    DateTime? CompletedAt);

public record AdminPaymentResponse(
    Guid Id,
    Guid OrderId,
    Guid CustomerId,
    decimal Amount,
    string PaymentMethod,
    string Status,
    string? ProviderTransactionId,
    string? FailureReason,
    DateTime CreatedAt,
    DateTime? CompletedAt);
