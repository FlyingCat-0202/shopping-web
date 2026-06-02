using EventBus.Contracts;
using MassTransit;
using Product.API.Products;

namespace Product.API.IntegrationEvents.Consumers.Self;

internal sealed class ProductDeleteConsumer(
    IProductMutationService products,
    ILogger<ProductDeleteConsumer> logger)
    : IConsumer<DeleteProductRequest>
{
    public async Task Consume(ConsumeContext<DeleteProductRequest> context)
    {
        try
        {
            await products.DeleteProductAsync(context.Message, context.CancellationToken);
            logger.LogInformation("Đã xóa/ẩn sản phẩm {ProductId}.", context.Message.Id);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Lỗi khi xóa/ẩn sản phẩm {ProductId}.", context.Message.Id);
            throw;
        }
    }
}
