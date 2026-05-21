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
            // 1. Cập nhật tất cả các trường vào PostgreSQL (Dùng ExecuteUpdateAsync cho hiệu năng cực cao)
            var affectedRow = await db.Products
                .Where(p => p.Id == msg.Id)
                .ExecuteUpdateAsync(u => u
                    .SetProperty(p => p.Name, msg.Name)
                    .SetProperty(p => p.Price, msg.Price)
                    .SetProperty(p => p.StockQuantity, msg.StockQuantity)
                    .SetProperty(p => p.IsActive, msg.IsActive)
                    .SetProperty(p => p.Description, msg.Description)
                    .SetProperty(p => p.ImageUrl, msg.ImgUrl)
                    .SetProperty(p => p.CategoryId, msg.CategoryId),
                context.CancellationToken);

            // Nếu không có dòng nào được update (sản phẩm có thể đã bị xóa trước đó) -> Dừng luôn
            if (affectedRow == 0)
            {
                logger.LogWarning("Không tìm thấy sản phẩm {product_id} trong database để cập nhật", msg.Id);
                return;
            }

            // 2. Lấy Tên Category từ Database để chuẩn bị cho Elasticsearch
            // (Chỉ Select đúng trường Name để tối ưu tốc độ)
            var categoryName = await db.Categories
                .Where(c => c.Id == msg.CategoryId)
                .Select(c => c.Name)
                .FirstOrDefaultAsync(context.CancellationToken) ?? "";

            // 3. Thiết lập data để bắn vào queue Elastic 
            var eventMsg = new ProductUpdatedEvent(
                Id: msg.Id,
                Name: msg.Name,
                Price: msg.Price,
                IsActive: msg.IsActive,
                CategoryName: categoryName
            );

            // 4. Publish Event cho Consumer của Elastic hứng
            await pe.Publish(eventMsg, context.CancellationToken);

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