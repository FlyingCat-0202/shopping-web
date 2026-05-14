using EventBus.Contracts;
using MassTransit;
using Product.Infrastructure.Data;

namespace Product.API.IntegrationEvents.Consumers.Self;
public class ProductDeleteConsumer(ProductDbContext db, ILogger<ProductDeleteConsumer> logger) : IConsumer<DeleteProductRequest>
{
    public async Task Consume(ConsumeContext<DeleteProductRequest> context)
    {
        var msg = context.Message;
        if (logger.IsEnabled(LogLevel.Information))
        {
            logger.LogInformation("{time} Thực hiện xóa sản phẩm {id}", DateTime.UtcNow, msg.Id);
        }

        try
        {
            var product = await db.Products.FindAsync([msg.Id], context.CancellationToken);

            if (product is null)
            {
                if (logger.IsEnabled(LogLevel.Information))
                {
                    logger.LogInformation("{DateTime.UtcNow} Không tìm thấy sản phẩm xóa: {msg.Id} ", DateTime.UtcNow, msg.Id);
                }
                return;
            }

            product.IsActive = false;
            product.StockQuantity = 0;

            await db.SaveChangesAsync(context.CancellationToken);
            if (logger.IsEnabled(LogLevel.Information))
            {
                logger.LogInformation("{time} Xóa sản phẩm {Name} thành công", DateTime.UtcNow, product.Name);
            }
        }
        catch (Exception ex)
        {
            if (logger.IsEnabled(LogLevel.Error))
            {
                logger.LogError(ex, "Lỗi không thể xóa sản phẩm {Id}", msg.Id);
            }
            throw;
        }
    }
}
