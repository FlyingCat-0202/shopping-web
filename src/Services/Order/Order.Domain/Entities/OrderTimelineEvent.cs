namespace Order.Domain.Entities;

public class OrderTimelineEvent
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid OrderId { get; private set; }
    public Order Order { get; private set; } = null!;
    public string Status { get; private set; } = string.Empty;
    public string Title { get; private set; } = string.Empty;
    public string Description { get; private set; } = string.Empty;
    public string Source { get; private set; } = string.Empty;
    public DateTime OccurredAt { get; private set; } = DateTime.UtcNow;

    private OrderTimelineEvent()
    {
    }

    internal OrderTimelineEvent(
        Guid orderId,
        string status,
        string title,
        string description,
        string source,
        DateTime? occurredAt = null)
    {
        OrderId = orderId;
        Status = string.IsNullOrWhiteSpace(status) ? "Unknown" : status.Trim();
        Title = string.IsNullOrWhiteSpace(title) ? Status : title.Trim();
        Description = string.IsNullOrWhiteSpace(description) ? Title : description.Trim();
        Source = string.IsNullOrWhiteSpace(source) ? "Order" : source.Trim();
        OccurredAt = occurredAt ?? DateTime.UtcNow;
    }
}