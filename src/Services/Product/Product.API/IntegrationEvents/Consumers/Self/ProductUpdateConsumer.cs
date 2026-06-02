using EventBus.Contracts;
using MassTransit;
using Product.API.Products;

namespace Product.API.IntegrationEvents.Consumers.Self;

internal sealed class ProductUpdateConsumer(
    IProductMutationService products,
    ILogger<ProductUpdateConsumer> logger)
    : IConsumer<UpdateProductRequest>
{
    public async Task Consume(ConsumeContext<UpdateProductRequest> context)
    {
        try
        {
            await products.UpdateProductAsync(context.Message, context.CancellationToken);
            logger.LogInformation("Đã cập nhật sản phẩm {ProductId}.", context.Message.Id);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Lỗi khi cập nhật sản phẩm {ProductId}.", context.Message.Id);
            throw;
        }
    }
}
