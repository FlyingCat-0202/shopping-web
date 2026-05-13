using Cart.Infrastructure.Data;
using EventBus.Contracts;
using MassTransit;
using Microsoft.EntityFrameworkCore;

namespace Cart.API.IntegrationEvents.Consumers;

public class ProductDeletedConsumer(CartDbContext dbContext, ILogger<ProductDeletedConsumer> logger)
    : IConsumer<ProductDeletedEvent>
{
    public async Task Consume(ConsumeContext<ProductDeletedEvent> context)
    {
        var removed = await dbContext.CartItems
            .Where(c => c.ProductId == context.Message.ProductId)
            .ExecuteDeleteAsync(context.CancellationToken);

        logger.LogInformation(
            "Đã xóa {Count} cart items liên quan product {ProductId}.",
            removed,
            context.Message.ProductId);
    }
}
