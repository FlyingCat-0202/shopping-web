using EventBus.Contracts;
using EventBus.Extensions;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Payment.Domain.Entities;
using Payment.Domain.Enums;
using Payment.Infrastructure.Data;
using System.Security.Claims;

namespace Payment.API.Endpoints;

public static class PaymentEndpoints
{
    private const string PaymentSucceededRoutingKey = "payment-succeeded";
    private const string PaymentFailedRoutingKey = "payment-failed";

    public static void MapPaymentEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/payment")
            .WithTags("Payments")
            .RequireAuthorization();
        
        group.MapGet("/order/{orderId:guid}", GetPaymentByOrderId)
            .WithName("GetPaymentByOrderId");

        group.MapPost("/{id:guid}/confirm", ConfirmPayment)
            .WithName("ConfirmPayment");

        var admin = group.MapGroup("/admin")
            .RequireAuthorization(EndpointHelpers.AdminOnly);

        admin.MapGet("/{id:guid}", GetPaymentById)
            .WithName("GetAdminPaymentById");

        app.MapPost("/api/payment/webhook", ConfirmPaymentWebhook)
            .WithTags("Payments")
            .AllowAnonymous()
            .WithName("ConfirmPaymentWebhook");
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

    private static async Task<IResult> ConfirmPaymentWebhook(
        PaymentWebhookRequest request,
        PaymentDbContext db,
        IPublishEndpoint publishEndpoint,
        CancellationToken cancellationToken)
    {
        if (request.PaymentId == Guid.Empty)
            return Results.BadRequest(new { message = "PaymentId không hợp lệ." });

        var payment = await db.Payments
            .FirstOrDefaultAsync(p => p.Id == request.PaymentId, cancellationToken);

        if (payment is null)
            return Results.NotFound();

        if (payment.Status == PaymentStatus.Succeeded)
        {
            await PublishSucceeded(publishEndpoint, payment, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            return Results.Ok(ToPaymentResponse(payment));
        }

        if (payment.Status == PaymentStatus.Failed)
        {
            await PublishFailed(publishEndpoint, payment, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            return Results.Ok(ToPaymentResponse(payment));
        }

        if (request.Success)
        {
            payment.MarkSucceeded(
                string.IsNullOrWhiteSpace(request.ProviderTransactionId)
                    ? $"webhook-{payment.Id:N}"
                    : request.ProviderTransactionId.Trim());

            await PublishSucceeded(publishEndpoint, payment, cancellationToken);
        }
        else
        {
            payment.MarkFailed(request.Reason ?? "Cổng thanh toán báo thất bại.");
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
}

public record PaymentWebhookRequest(
    Guid PaymentId,
    bool Success,
    string? ProviderTransactionId,
    string? Reason);

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
