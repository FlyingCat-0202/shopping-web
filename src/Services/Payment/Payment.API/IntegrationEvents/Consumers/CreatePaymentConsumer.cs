using EventBus.Contracts;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Payment.Domain.Entities;
using Payment.Infrastructure.Data;

namespace Payment.API.IntegrationEvents.Consumers;

public class CreatePaymentConsumer(PaymentDbContext dbContext, ILogger<CreatePaymentConsumer> logger)
    : IConsumer<CreatePaymentCommand>
{
    public async Task Consume(ConsumeContext<CreatePaymentCommand> context)
    {
        try
        {
            var message = context.Message;

            var payment = await dbContext.Payments
                .FirstOrDefaultAsync(p => p.OrderId == message.OrderId, context.CancellationToken);

            if (payment is not null)
            {
                if (!MatchesRequest(payment, message))
                {
                    throw new InvalidOperationException(
                        $"Payment cho Order {message.OrderId} đã tồn tại nhưng dữ liệu không khớp với CreatePaymentCommand.");
                }

                logger.LogInformation(
                    "Bỏ qua CreatePaymentCommand duplicate cho Order {OrderId}; payment {PaymentId} đã tồn tại ở trạng thái {Status}.",
                    payment.OrderId,
                    payment.Id,
                    payment.Status);
                await PublishPaymentCreated(context, payment);
                await dbContext.SaveChangesAsync(context.CancellationToken);
                return;
            }

            payment = PaymentTransaction.Create(
                message.OrderId,
                message.CustomerId,
                message.Amount,
                message.PaymentMethod);

            dbContext.Payments.Add(payment);
            await PublishPaymentCreated(context, payment);

            await dbContext.SaveChangesAsync(context.CancellationToken);
            logger.LogInformation(
                "Payment {PaymentId} cho Order {OrderId} đang ở trạng thái {Status}.",
                payment.Id,
                payment.OrderId,
                payment.Status);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Lỗi khi xử lý CreatePaymentCommand cho Order {OrderId}", context.Message.OrderId);
            throw;
        }
    }

    private static bool MatchesRequest(PaymentTransaction payment, CreatePaymentCommand message)
        => payment.CustomerId == message.CustomerId &&
           payment.Amount == message.Amount &&
           string.Equals(
               payment.PaymentMethod,
               message.PaymentMethod.Trim(),
               StringComparison.OrdinalIgnoreCase);

    private static Task PublishPaymentCreated(ConsumeContext<CreatePaymentCommand> context, PaymentTransaction payment)
        => context.Publish(new PaymentCreatedEvent
        {
            CorrelationId = context.Message.CorrelationId,
            PaymentId = payment.Id,
            OrderId = payment.OrderId,
            CustomerId = payment.CustomerId,
            Amount = payment.Amount,
            PaymentMethod = payment.PaymentMethod,
            Status = payment.Status.ToString()
        }, context.CancellationToken);
}
