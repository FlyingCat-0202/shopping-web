using EventBus.Contracts;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Order.Infrastructure.Data;

namespace Order.API.IntegrationEvents.Consumer;
public class StockReservationFailedConsumer(OrderDbContext dbContext) : IConsumer<StockReservationFailedEvent>
{
    public async Task Consume(ConsumeContext<StockReservationFailedEvent> context)
    {
        var order = await dbContext.Orders.FirstOrDefaultAsync(o => o.Id == context.Message.OrderId);
        if (order is null) return;
        if (order.Status == Domain.Enums.OrderStatus.Pending)
        {
            order.CancelDueToStockFailure();
            await dbContext.SaveChangesAsync();
        }
    }
}
