using EventBus.Contracts;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Product.Infrastructure.Data;

namespace Product.API.IntegrationEvents.Consumers.OrderSupportConsumer;

public class ReserveStockConsumer(
    ProductDbContext dbContext,
    IStockReservationService stockReservationService,
    ILogger<ReserveStockConsumer> logger) : IConsumer<ReserveStockCommand>
{
    public async Task Consume(ConsumeContext<ReserveStockCommand> context)
    {
        var message = context.Message;
        try
        {
            if (logger.IsEnabled(LogLevel.Information))
            {
                logger.LogInformation("Nhận yêu cầu trừ kho cho Order {OrderId}", message.OrderId);
            }

            var result = await stockReservationService.ReserveStockAsync(
                message.OrderId,
                message.Items,
                context.CancellationToken);

            if (result.Status == StockReservationResultStatus.Failed)
            {
                logger.LogWarning(
                    "Giữ kho thất bại cho Order {OrderId}: {Reason}",
                    message.OrderId,
                    result.FailureReason);

                await PublishStockReservationFailedAsync(
                    context,
                    message,
                    result.FailureReason ?? "Không thể giữ kho cho đơn hàng.");

                await dbContext.SaveChangesAsync(context.CancellationToken);
                return;
            }

            if (result.Status == StockReservationResultStatus.Ignored)
            {
                logger.LogInformation(
                    "Order {OrderId} đã release stock reservation, bỏ qua duplicate ReserveStockCommand.",
                    message.OrderId);
                return;
            }

            await PublishStockReservedAsync(context, message, result.Items);
            
            await dbContext.SaveChangesAsync(context.CancellationToken);
            if (logger.IsEnabled(LogLevel.Information))
            {
                logger.LogInformation("Trừ kho thành công cho Order {OrderId}", message.OrderId);
            }
        }
        catch (DbUpdateConcurrencyException)
        {
            logger.LogWarning("Tranh chấp dữ liệu kho cho Order {OrderId}", message.OrderId);
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Lỗi kỹ thuật khi giữ kho cho Order {OrderId}", message.OrderId);
            throw;
        }
    }

    private static Task PublishStockReservationFailedAsync(
        ConsumeContext<ReserveStockCommand> context,
        ReserveStockCommand command,
        string reason)
        => context.Publish(new StockReservationFailedEvent
        {
            CorrelationId = command.CorrelationId,
            OrderId = command.OrderId,
            CustomerId = command.CustomerId,
            Reason = reason
        }, context.CancellationToken);

    private static Task PublishStockReservedAsync(
        ConsumeContext<ReserveStockCommand> context,
        ReserveStockCommand command,
        IReadOnlyCollection<ValidatedOrderItem> items)
        => context.Publish(new StockReservedEvent
        {
            CorrelationId = command.CorrelationId,
            OrderId = command.OrderId,
            CustomerId = command.CustomerId,
            Items = [.. items]
        }, context.CancellationToken);
}
