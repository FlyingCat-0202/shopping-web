using EventBus.Infrastructure;
using Product.Infrastructure.Data;

namespace Product.Infrastructure.Idempotency;

public class ProductIdempotencyService(ProductDbContext dbContext) : IIdempotencyService
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
