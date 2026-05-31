using EventBus.Contracts;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Product.Infrastructure.Data;

namespace Product.API.IntegrationEvents.Consumers.Self;

public class ProductUpdateConsumer(ProductDbContext db, ILogger<ProductUpdateConsumer> logger, IPublishEndpoint pe) : IConsumer<UpdateProductRequest>
{
    public async Task Consume(ConsumeContext<UpdateProductRequest> context)
    {
        var msg = context.Message;

        if (logger.IsEnabled(LogLevel.Information))
        {
            logger.LogInformation("Bắt đầu cập nhật toàn bộ thông tin cho sản phẩm {product_id}", msg.Id);
        }

        try
        {
            var product = await db.Products
                .FirstOrDefaultAsync(p => p.Id == msg.Id, context.CancellationToken);

            if (product is null)
            {
                logger.LogWarning("Không tìm thấy sản phẩm {product_id} trong database để cập nhật", msg.Id);
                return;
            }

            var categoryName = await db.Categories
                .Where(c => c.Id == msg.CategoryId)
                .Select(c => c.Name)
                .FirstOrDefaultAsync(context.CancellationToken);

            if (categoryName is null)
                throw new InvalidOperationException($"Category {msg.CategoryId} không tồn tại.");

            product.Name = msg.Name.Trim();
            product.Price = msg.Price;
            product.StockQuantity = msg.StockQuantity;
            product.IsActive = msg.IsActive;
            product.Description = string.IsNullOrWhiteSpace(msg.Description) ? null : msg.Description.Trim();
            product.ImageUrl = string.IsNullOrWhiteSpace(msg.ImgUrl) ? null : msg.ImgUrl.Trim();
            product.CategoryId = msg.CategoryId;

            var eventMsg = new ProductUpdatedEvent(
                Id: msg.Id,
                Name: product.Name,
                Price: msg.Price,
                IsActive: msg.IsActive,
                CategoryName: categoryName,
                Description: product.Description,
                ImageUrl: product.ImageUrl,
                CategoryId: product.CategoryId,
                StockQuantity: product.StockQuantity
            );

            await pe.Publish(eventMsg, context.CancellationToken);
            await db.SaveChangesAsync(context.CancellationToken);

            if (logger.IsEnabled(LogLevel.Information))
            {
                logger.LogInformation("Cập nhật thành công cho sản phẩm {product_id} và đã đẩy event", msg.Id);
            }
        }
        catch (Exception ex)
        {
            if (logger.IsEnabled(LogLevel.Error))
            {
                logger.LogError(ex, "Lỗi khi cập nhật dữ liệu cho sản phẩm {Id}", msg.Id);
            }
            throw; // Ném lỗi ra để MassTransit biết và đưa vào Retry/Error Queue
        }
    }
}
