namespace EventBus.Infrastructure;

public interface IIdempotencyService
{
    Task<bool> RequestExistsAsync(Guid requestId);
    Task CreateRequestAsync(Guid requestId);
}