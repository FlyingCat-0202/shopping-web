using EventBus.Contracts;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Payment.Domain.Entities;
using Payment.Infrastructure.Data;

namespace Payment.API.IntegrationEvents.Consumers;

public class PaymentRequestedConsumer(PaymentDbContext dbContext, ILogger<PaymentRequestedConsumer> logger)
    : IConsumer<PaymentRequestedEvent>
{
    public async Task Consume(ConsumeContext<PaymentRequestedEvent> context)
    {
        try
        {
            var message = context.Message;

            var payment = await dbContext.Payments
                .FirstOrDefaultAsync(p => p.OrderId == message.OrderId, context.CancellationToken);

            if (payment is null)
            {
                payment = PaymentTransaction.Create(
                    message.OrderId,
                    message.CustomerId,
                    message.Amount,
                    message.PaymentMethod);

                dbContext.Payments.Add(payment);
            }

            await dbContext.SaveChangesAsync(context.CancellationToken);
            logger.LogInformation(
                "Payment {PaymentId} cho Order {OrderId} đang ở trạng thái {Status}.",
                payment.Id,
                payment.OrderId,
                payment.Status);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Lỗi khi xử lý PaymentRequestedEvent cho Order {OrderId}", context.Message.OrderId);
            throw;
        }
    }
}
