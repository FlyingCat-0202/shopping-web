using Cart.Infrastructure.Data;
using EventBus.Infrastructure;
using Order.Infrastructure.Data;

namespace Cart.Infrastructure.Idempotency;

public class CartIdempotencyService(CartDbContext dbContext) : IIdempotencyService
{
    public async Task<bool> RequestExistsAsync(Guid requestId)
    {
        return await dbContext.IdempotentRequests.FindAsync(requestId) is not null;
    }

    public Task CreateRequestAsync(Guid requestId)
    {
        dbContext.IdempotentRequests.Add(new IdempotentRequest { Id = requestId });
        return Task.CompletedTask;
    }
}
