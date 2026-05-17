using EventBus.Contracts;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Order.Domain.Enums;
using Order.Infrastructure.Data;

namespace Order.API.IntegrationEvents.Consumer;

public class PaymentSucceededConsumer(OrderDbContext dbContext, ILogger<PaymentSucceededConsumer> logger)
    : IConsumer<PaymentSucceededEvent>
{
    private const string CartItemsRemovedRoutingKey = "cart-items-removed";

    public async Task Consume(ConsumeContext<PaymentSucceededEvent> context)
    {
        try
        {
            var message = context.Message;
            var order = await dbContext.Orders
                .Include(o => o.Items)
                .FirstOrDefaultAsync(o => o.Id == message.OrderId, context.CancellationToken);

            if (order is null)
                return;

            if (order.Status == OrderStatus.PaymentPending)
            {
                order.ConfirmPayment();

                var productIds = order.Items.Select(od => od.ProductId).ToList();
                await context.Publish(new CartItemsRemovedEvent
                {
                    CustomerId = order.CustomerId,
                    ProductIds = productIds
                }, ctx => ctx.SetRoutingKey(CartItemsRemovedRoutingKey), context.CancellationToken);

                await dbContext.SaveChangesAsync(context.CancellationToken);

                logger.LogInformation(
                    "Order {OrderId} đã thanh toán thành công và chuyển sang Processing.",
                    order.Id);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Lỗi khi xử lý PaymentSucceededEvent cho Order {OrderId}", context.Message.OrderId);
            throw;
        }
    }
}
