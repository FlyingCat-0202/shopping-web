using Cart.API.CartStore;
using EventBus.Contracts;
using MassTransit;

namespace Cart.API.IntegrationEvents.Consumers;

public class RemoveCartItemsConsumer(ICartStore cartStore, ILogger<RemoveCartItemsConsumer> logger)
    : IConsumer<RemoveCartItemsCommand>
{
    public async Task Consume(ConsumeContext<RemoveCartItemsCommand> context)
    {
        try
        {
            var productIds = context.Message.ProductIds.Distinct().ToList();
            if (productIds.Count == 0)
                return;

            var removed = await cartStore.RemoveItemsAsync(context.Message.CustomerId, productIds);

            logger.LogInformation(
                "Đã xóa {Count} cart items của customer {CustomerId} sau khi đơn hàng được xác nhận.",
                removed,
                context.Message.CustomerId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Lỗi khi xóa cart items cho customer {CustomerId}", context.Message.CustomerId);
            throw;
        }
    }
}
