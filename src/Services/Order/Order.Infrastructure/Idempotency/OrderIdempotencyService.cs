using EventBus.Infrastructure;
using Order.Infrastructure.Data;

namespace Order.Infrastructure.Idempotency;

public class OrderIdempotencyService(OrderDbContext dbContext) : IIdempotencyService
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
