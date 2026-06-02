using EventBus.Contracts;
using MassTransit;
using Product.API.Products;

namespace Product.API.IntegrationEvents.Consumers.Self;

internal sealed class ProductCreationConsumer(
    IProductMutationService products,
    ILogger<ProductCreationConsumer> logger)
    : IConsumer<CreateProductRequest>
{
    public async Task Consume(ConsumeContext<CreateProductRequest> context)
    {
        try
        {
            await products.CreateProductAsync(context.Message, context.CancellationToken);
            logger.LogInformation("Đã tạo sản phẩm {Name}.", context.Message.Name);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Lỗi khi tạo sản phẩm {Name}.", context.Message.Name);
            throw;
        }
    }
}
