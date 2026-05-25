using EventBus.Contracts;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Product.Infrastructure.Data;
using ProductEntity = Product.Domain.Entities.Product;

namespace Product.API.IntegrationEvents.Consumers.Self;

public class ProductCreationConsumer(ProductDbContext db, ILogger<ProductCreationConsumer> logger, IPublishEndpoint pe) : IConsumer<CreateProductRequest>
{
    public async Task Consume(ConsumeContext<CreateProductRequest> context)
    {
        var data = context.Message;
        try
        {
            if (logger.IsEnabled(LogLevel.Information))
            {
                logger.LogInformation("Đang tạo sản phẩm mới: {Name}", data.Name);
            }


            var newProduct = new ProductEntity
            {
                Id = Guid.NewGuid(),
                Name = data.Name.Trim(),
                Price = data.Price,
                StockQuantity = data.StockQuantity,
                Description = string.IsNullOrWhiteSpace(data.Description)
                    ? null
                    : data.Description.Trim(),
                ImageUrl = string.IsNullOrWhiteSpace(data.ImageUrl)
                    ? null
                    : data.ImageUrl.Trim(),
                CategoryId = data.CategoryId,
                IsActive = data.IsActive,
            };

            db.Products.Add(newProduct);

            // Thiết lập data để bắn vào queue của Elasticsearch
            var category = await db.Categories.FirstOrDefaultAsync(c => c.Id == data.CategoryId) ?? throw new Exception("Category không tồn tại!");

            var eventMsg = new ProductCreatedEvent
            (
                newProduct.Id,
                newProduct.Name,
                newProduct.Description!,
                newProduct.Price,
                category.Name,
                newProduct.IsActive,
                newProduct.ImageUrl
            );
            await pe.Publish(eventMsg, context.CancellationToken);

            await db.SaveChangesAsync(context.CancellationToken);

            if (logger.IsEnabled(LogLevel.Information))
            {
                logger.LogInformation("Đã lưu sản phẩm {Name} thành công với ID: {Id}", data.Name, newProduct.Id);
            }
        }
        catch (Exception ex)
        {
            if (logger.IsEnabled(LogLevel.Error))
            {
                logger.LogError(ex, "Lỗi khi lưu sản phẩm {Name} vào Database", data.Name);
            }
            throw;
        }
    }
}
