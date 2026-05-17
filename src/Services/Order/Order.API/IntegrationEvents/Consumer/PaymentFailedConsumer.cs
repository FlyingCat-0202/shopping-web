using EventBus.Contracts;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Order.Domain.Enums;
using Order.Infrastructure.Data;

namespace Order.API.IntegrationEvents.Consumer;

public class PaymentFailedConsumer(OrderDbContext dbContext, ILogger<PaymentFailedConsumer> logger)
    : IConsumer<PaymentFailedEvent>
{
    private const string OrderCancelledRoutingKey = "order-cancelled";

    public async Task Consume(ConsumeContext<PaymentFailedEvent> context)
    {
        try
        {
            var message = context.Message;
            var order = await dbContext.Orders
                .Include(o => o.Items)
                .FirstOrDefaultAsync(o => o.Id == message.OrderId, context.CancellationToken);

            if (order is null)
                return;

            if (order.Status != OrderStatus.PaymentPending)
                return;

            order.CancelDueToPaymentFailure();

            var items = order.Items
                .Select(od => new OrderItemInfo { ProductId = od.ProductId, Quantity = od.Quantity })
                .ToList();

            await context.Publish(new OrderCancelledEvent { OrderId = order.Id, Items = items },
                ctx => ctx.SetRoutingKey(OrderCancelledRoutingKey),
                context.CancellationToken);

            await dbContext.SaveChangesAsync(context.CancellationToken);

            logger.LogWarning(
                "Order {OrderId} đã bị hủy vì thanh toán thất bại: {Reason}",
                order.Id,
                message.Reason);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Lỗi khi xử lý PaymentFailedEvent cho Order {OrderId}", context.Message.OrderId);
            throw;
        }
    }
}
