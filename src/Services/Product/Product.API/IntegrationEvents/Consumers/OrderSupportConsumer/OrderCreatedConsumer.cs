using EventBus.Contracts;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Product.Infrastructure.Data;

namespace Product.API.IntegrationEvents.Consumers.OrderSupportConsumer;

public class OrderCreatedConsumer(ProductDbContext dbContext, ILogger<OrderCreatedConsumer> logger) : IConsumer<OrderCreatedEvent>
{
    public async Task Consume(ConsumeContext<OrderCreatedEvent> context)
    {
        var message = context.Message;
        try
        {
            if (logger.IsEnabled(LogLevel.Information))
            {
                logger.LogInformation("Nhận yêu cầu trừ kho cho Order {OrderId}", message.OrderId);
            }

            var productIds = message.Items.Select(x => x.ProductId).ToList();
            var products = await dbContext.Products
                .Where(p => productIds.Contains(p.Id) && p.IsActive)
                .ToListAsync(context.CancellationToken);

            if (products.Count != productIds.Count)
            {
                logger.LogWarning("Không tìm thấy một số sản phẩm cho Order {OrderId}", message.OrderId);
                await context.Publish(new StockReservationFailedEvent
                {
                    OrderId = message.OrderId,
                    Reason = "Sản phẩm không tồn tại hoặc đã ngừng kinh doanh."
                }, context.CancellationToken);
                await dbContext.SaveChangesAsync(context.CancellationToken);
                return;
            }

            var productById = products.ToDictionary(p => p.Id);

            foreach (var item in message.Items)
            {
                var p = productById[item.ProductId];
                if (item.Quantity <= 0 || item.Quantity > p.StockQuantity)
                {
                    logger.LogWarning("Sản phẩm {ProductId} không đủ hàng cho Order {OrderId}", p.Id, message.OrderId);
                    await context.Publish(new StockReservationFailedEvent
                    {
                        OrderId = message.OrderId,
                        Reason = $"Sản phẩm {p.Id} không đủ hàng."
                    }, context.CancellationToken);
                    await dbContext.SaveChangesAsync(context.CancellationToken);

                    return;
                }
            }

            foreach (var item in message.Items)
            {
                var p = productById[item.ProductId];
                p.StockQuantity -= item.Quantity;
            }

            await context.Publish(new StockReservedEvent
            {
                OrderId = message.OrderId,
                Items = [.. message.Items.Select(i => new ValidatedOrderItem
                {
                    ProductId = i.ProductId,
                    Quantity = i.Quantity,
                    UnitPrice = productById[i.ProductId].Price
                })]
            }, context.CancellationToken);

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
}
