namespace EventBus.Contracts;

public class RemoveCartItemsCommand
{
    public Guid CommandId { get; set; } = Guid.NewGuid();
    public Guid CorrelationId { get; set; }
    public Guid OrderId { get; set; }
    public Guid CustomerId { get; set; }
    public List<Guid> ProductIds { get; set; } = [];
    public DateTime RequestedAt { get; set; } = DateTime.UtcNow;
}
