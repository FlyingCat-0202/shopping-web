using EventBus.Contracts;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Product.Infrastructure.Data;

namespace Product.API.IntegrationEvents.Consumers.OrderSupportConsumer;

public class OrderReturnedConsumer(ProductDbContext dbContext, ILogger<OrderReturnedConsumer> logger) : IConsumer<OrderReturnedEvent>
{
    public async Task Consume(ConsumeContext<OrderReturnedEvent> context)
    {
        var message = context.Message;
        try
        {
            if (logger.IsEnabled(LogLevel.Information))
            {
                logger.LogInformation("Nhận yêu cầu hoàn kho cho đơn hàng đã trả {OrderId}", message.OrderId);
            }

            if (message.Items == null || message.Items.Count == 0)
            {
                logger.LogWarning("Đơn hàng {OrderId} không có sản phẩm để hoàn kho.", message.OrderId);
                return;
            }

            var productIds = message.Items.Select(x => x.ProductId).ToList();
            var products = await dbContext.Products
                .Where(p => productIds.Contains(p.Id))
                .ToListAsync(context.CancellationToken);

            if (products.Count != productIds.Count)
            {
                var foundProductIds = products.Select(p => p.Id).ToList();
                var missingProductIds = productIds.Except(foundProductIds).ToList();

                logger.LogError("Không tìm thấy một số sản phẩm để hoàn kho cho Order {OrderId}. Các ProductId thiếu: {MissingIds}",
                    message.OrderId, string.Join(", ", missingProductIds));
            }

            var productById = products.ToDictionary(p => p.Id);

            foreach (var item in message.Items)
            {
                if (productById.TryGetValue(item.ProductId, out var p))
                {
                    p.StockQuantity += item.Quantity;
                }
                else
                {
                    logger.LogWarning("Bỏ qua hoàn kho cho sản phẩm {ProductId} của Order {OrderId} vì không tìm thấy trong DB.",
                        item.ProductId, message.OrderId);
                }
            }

            await dbContext.SaveChangesAsync(context.CancellationToken);

            if (logger.IsEnabled(LogLevel.Information))
            {
                logger.LogInformation("Hoàn kho thành công cho Order {OrderId}", message.OrderId);
            }
        }
        catch (DbUpdateConcurrencyException)
        {
            logger.LogWarning("Tranh chấp dữ liệu khi hoàn kho cho đơn hàng đã trả {OrderId}", message.OrderId);
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Lỗi không xác định khi hoàn kho cho Order {OrderId}", message.OrderId);
            throw;
        }
    }
}
