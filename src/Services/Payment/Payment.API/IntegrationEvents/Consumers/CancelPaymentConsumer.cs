using EventBus.Contracts;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Payment.Domain.Entities;
using Payment.Domain.Enums;
using Payment.Infrastructure.Data;

namespace Payment.API.IntegrationEvents.Consumers;

public class CancelPaymentConsumer(PaymentDbContext dbContext, ILogger<CancelPaymentConsumer> logger)
    : IConsumer<CancelPaymentCommand>
{
    public async Task Consume(ConsumeContext<CancelPaymentCommand> context)
    {
        var message = context.Message;

        try
        {
            var payment = await dbContext.Payments
                .FirstOrDefaultAsync(p => p.OrderId == message.OrderId, context.CancellationToken);

            if (payment is null)
            {
                payment = PaymentTransaction.CreateFailed(
                    message.OrderId,
                    message.CustomerId,
                    message.Amount,
                    message.PaymentMethod,
                    message.Reason);

                dbContext.Payments.Add(payment);
                await PublishPaymentFailed(context, payment, payment.FailureReason);
                await dbContext.SaveChangesAsync(context.CancellationToken);
                return;
            }

            if (!MatchesRequest(payment, message))
            {
                throw new InvalidOperationException(
                    $"Payment cho Order {message.OrderId} đã tồn tại nhưng dữ liệu không khớp với CancelPaymentCommand.");
            }

            if (payment.Status == PaymentStatus.Pending)
            {
                payment.MarkFailed(message.Reason);
                await PublishPaymentFailed(context, payment, payment.FailureReason);
                await dbContext.SaveChangesAsync(context.CancellationToken);
                return;
            }

            await PublishCurrentPaymentState(context, payment);
            await dbContext.SaveChangesAsync(context.CancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Lỗi khi xử lý CancelPaymentCommand cho Order {OrderId}", message.OrderId);
            throw;
        }
    }

    private static bool MatchesRequest(PaymentTransaction payment, CancelPaymentCommand message)
        => payment.CustomerId == message.CustomerId &&
           payment.Amount == message.Amount &&
           string.Equals(
               payment.PaymentMethod,
               message.PaymentMethod.Trim(),
               StringComparison.OrdinalIgnoreCase);

    private static Task PublishCurrentPaymentState(
        ConsumeContext<CancelPaymentCommand> context,
        PaymentTransaction payment)
        => payment.Status == PaymentStatus.Succeeded
            ? context.Publish(new PaymentSucceededEvent
            {
                CorrelationId = context.Message.CorrelationId,
                PaymentId = payment.Id,
                OrderId = payment.OrderId,
                CustomerId = payment.CustomerId,
                Amount = payment.Amount,
                PaymentMethod = payment.PaymentMethod,
                ProviderTransactionId = payment.ProviderTransactionId ?? string.Empty
            }, context.CancellationToken)
            : PublishPaymentFailed(context, payment, payment.FailureReason);

    private static Task PublishPaymentFailed(
        ConsumeContext<CancelPaymentCommand> context,
        PaymentTransaction payment,
        string? reason)
        => context.Publish(new PaymentFailedEvent
        {
            CorrelationId = context.Message.CorrelationId,
            PaymentId = payment.Id,
            OrderId = payment.OrderId,
            CustomerId = payment.CustomerId,
            Reason = string.IsNullOrWhiteSpace(reason) ? "Payment was cancelled." : reason
        }, context.CancellationToken);
}
