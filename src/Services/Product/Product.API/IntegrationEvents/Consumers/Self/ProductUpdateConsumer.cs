using EventBus.Contracts;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Product.Infrastructure.Data;

namespace Product.API.IntegrationEvents.Consumers.Self;

public class ProductUpdateConsumer(ProductDbContext db, ILogger<ProductUpdateConsumer> logger) : IConsumer<UpdateProductRequest>
{
    public async Task Consume(ConsumeContext<UpdateProductRequest> context)
    {
        var msg = context.Message;
        if (logger.IsEnabled(LogLevel.Information))
        {
            logger.LogInformation("{time} thực hiện đổi danh mục cho sản phẩm {product_id} thành {category_id}", DateTime.UtcNow, msg.Id, msg.CategoryId);
        }
        try
        {
            var affectedRow = await db.Products
                                        .Where(p => p.Id == msg.Id)
                                        .ExecuteUpdateAsync(u => u
                                        .SetProperty(p => p.CategoryId, msg.CategoryId),
                                            context.CancellationToken);
            if (logger.IsEnabled(LogLevel.Information))
            {
                logger.LogInformation("Đổi danh mục cho sản phẩm {product_id} thành {category_id} thành công", msg.Id, msg.CategoryId);
            }
        }
        catch (Exception ex)
        {
            if (logger.IsEnabled(LogLevel.Error))
            {
                logger.LogError(ex, "Lỗi không thể thay đổi danh mục cho {Id} thành {category_id}", msg.Id, msg.CategoryId);
            }
            throw;
        }
    }
}
