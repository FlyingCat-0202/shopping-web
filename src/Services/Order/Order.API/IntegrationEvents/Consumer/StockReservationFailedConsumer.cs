using EventBus.Contracts;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Order.Domain.Enums;
using Order.Infrastructure.Data;

namespace Order.API.IntegrationEvents.Consumer;
public class StockReservationFailedConsumer(
    OrderDbContext dbContext,
    ILogger<StockReservationFailedConsumer> logger)
    : IConsumer<StockReservationFailedEvent>
{
    private const string OrderStatusChangedRoutingKey = "order-status-changed";

    public async Task Consume(ConsumeContext<StockReservationFailedEvent> context)
    {
        try
        {
            var message = context.Message;
            var order = await dbContext.Orders.FirstOrDefaultAsync(
                o => o.Id == message.OrderId,
                context.CancellationToken);

            if (order is null)
                return;

            if (order.Status == OrderStatus.Pending)
            {
                var oldStatus = order.Status;
                order.CancelDueToStockFailure();
                await context.Publish(new OrderStatusChangedEvent
                {
                    OrderId = order.Id,
                    CustomerId = order.CustomerId,
                    OldStatus = oldStatus.ToString(),
                    NewStatus = order.Status.ToString(),
                    Reason = $"Stock reservation failed: {message.Reason}"
                }, ctx => ctx.SetRoutingKey(OrderStatusChangedRoutingKey), context.CancellationToken);

                await dbContext.SaveChangesAsync(context.CancellationToken);

                logger.LogWarning(
                    "Order {OrderId} đã bị hủy vì giữ kho thất bại: {Reason}",
                    order.Id,
                    message.Reason);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Lỗi khi xử lý StockReservationFailedEvent cho Order {OrderId}",
                context.Message.OrderId);
            throw;
        }
    }
}
