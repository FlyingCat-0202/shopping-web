using EventBus.Contracts;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Product.Infrastructure.Data;

namespace Product.API.IntegrationEvents.Consumers.OrderSupportConsumer;

public class ReleaseStockConsumer(
    ProductDbContext dbContext,
    IStockReservationService stockReservationService,
    ILogger<ReleaseStockConsumer> logger) : IConsumer<ReleaseStockCommand>
{
    public async Task Consume(ConsumeContext<ReleaseStockCommand> context)
    {
        var message = context.Message;

        try
        {
            if (logger.IsEnabled(LogLevel.Information))
            {
                logger.LogInformation("Nhận lệnh hoàn kho cho Order {OrderId}: {Reason}", message.OrderId, message.Reason);
            }

            var releasedCount = await stockReservationService.ReleaseReservedStockAsync(
                message.OrderId,
                message.Items,
                message.Reason,
                context.CancellationToken);

            await context.Publish(new StockReleasedEvent
            {
                CorrelationId = message.CorrelationId,
                OrderId = message.OrderId,
                CustomerId = message.CustomerId,
                Reason = message.Reason
            }, context.CancellationToken);

            await dbContext.SaveChangesAsync(context.CancellationToken);

            if (logger.IsEnabled(LogLevel.Information))
            {
                logger.LogInformation(
                    "Hoàn kho {ReleasedCount} reservation cho Order {OrderId}",
                    releasedCount,
                    message.OrderId);
            }
        }
        catch (DbUpdateConcurrencyException)
        {
            logger.LogWarning("Tranh chấp dữ liệu khi hoàn kho cho Order {OrderId}", message.OrderId);
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Lỗi không xác định khi hoàn kho cho Order {OrderId}", message.OrderId);
            throw;
        }
    }
}
