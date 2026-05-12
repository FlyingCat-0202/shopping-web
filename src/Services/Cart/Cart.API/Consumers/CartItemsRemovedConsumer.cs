using Cart.Infrastructure.Data;
using EventBus.Contracts;
using MassTransit;
using Microsoft.EntityFrameworkCore;

namespace Cart.API.Consumers;

public class CartItemsRemovedConsumer(CartDbContext dbContext, ILogger<CartItemsRemovedConsumer> logger)
    : IConsumer<CartItemsRemovedEvent>
{
    public async Task Consume(ConsumeContext<CartItemsRemovedEvent> context)
    {
        var productIds = context.Message.ProductIds.Distinct().ToList();
        if (productIds.Count == 0)
            return;

        var removed = await dbContext.CartItems
            .Where(c => c.CustomerId == context.Message.CustomerId && productIds.Contains(c.ProductId))
            .ExecuteDeleteAsync(context.CancellationToken);

        logger.LogInformation(
            "Đã xóa {Count} cart items của customer {CustomerId} sau khi đơn hàng được xác nhận.",
            removed,
            context.Message.CustomerId);
    }
}
