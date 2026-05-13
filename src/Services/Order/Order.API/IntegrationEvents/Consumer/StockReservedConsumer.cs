using EventBus.Contracts;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Order.Infrastructure.Data;

namespace Order.API.IntegrationEvents.Consumer;

public class StockReservedConsumer(OrderDbContext dbContext, ILogger<StockReservedConsumer> logger)
    : IConsumer<StockReservedEvent>
{
    public async Task Consume(ConsumeContext<StockReservedEvent> context)
    {
        var message = context.Message;
        var order = await dbContext.Orders
            .Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.Id == message.OrderId);

        if (order is null) return; // idempotent

        if (order.Status == Domain.Enums.OrderStatus.Pending)
        {
            var prices = message.Items.ToDictionary(i => i.ProductId, i => i.UnitPrice);
            order.ConfirmWithPrices(prices);

            var productIds = order.Items.Select(od => od.ProductId).ToList();
            await context.Publish(new CartItemsRemovedEvent
            {
                CustomerId = order.CustomerId,
                ProductIds = productIds
            });

            await dbContext.SaveChangesAsync();
            logger.LogInformation("Order {OrderId} đã được xác nhận với TotalAmount = {TotalAmount}",
                order.Id, order.TotalAmount);
        }
        else if (order.Status == Domain.Enums.OrderStatus.Cancelled)
        {
            var items = order.Items
                .Select(od => new OrderItemInfo { ProductId = od.ProductId, Quantity = od.Quantity })
                .ToList();

            await context.Publish(new OrderCancelledEvent { OrderId = order.Id, Items = items },
                ctx => ctx.SetRoutingKey("order-cancelled"));
        }
    }
}
