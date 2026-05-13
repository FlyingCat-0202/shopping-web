using EventBus.Contracts;
using MassTransit;
using Product.Infrastructure.Data;
using ProductEntity = Product.Domain.Entities.Product;

namespace Product.API.IntegrationEvents.Consumers.Self;

public class ProductCreationConsumer(ProductDbContext db, ILogger<ProductCreationConsumer> logger) : IConsumer<CreateProductRequest>
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
                Name = data.Name,
                Price = data.Price,
                StockQuantity = data.StockQuantity,
                CategoryId = data.CategoryId,
                IsActive = true,
            };

            db.Products.Add(newProduct);
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
