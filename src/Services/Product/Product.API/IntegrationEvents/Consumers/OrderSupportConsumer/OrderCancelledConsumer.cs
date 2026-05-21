using EventBus.Contracts;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Product.Infrastructure.Data;

namespace Product.API.IntegrationEvents.Consumers.OrderSupportConsumer;

public class OrderCancelledConsumer(ProductDbContext dbContext, ILogger<OrderCancelledConsumer> logger) : IConsumer<OrderCancelledEvent>
{
    public async Task Consume(ConsumeContext<OrderCancelledEvent> context)
    {
        var message = context.Message;
        try
        {
            if (logger.IsEnabled(LogLevel.Information))
            {
                logger.LogInformation("Nhận yêu cầu hoàn kho cho đơn hàng đã hủy {OrderId}", message.OrderId);
            }

            if (message.Items == null || message.Items.Count == 0)
            {
                logger.LogWarning("Đơn hàng {OrderId} không có sản phẩm để hoàn kho.", message.OrderId);
                return;
            }

            var releasedCount = await StockReservationReleaseHelper.ReleaseReservedStockAsync(
                dbContext,
                message.OrderId,
                message.Items,
                "Cancelled",
                context.CancellationToken);

            await dbContext.SaveChangesAsync(context.CancellationToken);

            if (logger.IsEnabled(LogLevel.Information))
            {
                logger.LogInformation("Hoàn kho {ReleasedCount} reservation cho Order {OrderId}", releasedCount, message.OrderId);
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
