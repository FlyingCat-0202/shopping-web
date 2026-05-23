namespace EventBus.Contracts;

public class OrderItemInfo
{
    public Guid ProductId { get; set; }
    public int Quantity { get; set; }
}

public class OrderSubmittedEvent
{
    public Guid CorrelationId { get; set; }
    public Guid OrderId { get; set; }
    public Guid CustomerId { get; set; }
    public string PaymentMethod { get; set; } = string.Empty;
    public List<OrderItemInfo> Items { get; set; } = [];
    public DateTime OccurredAt { get; set; } = DateTime.UtcNow;
}

public class OrderStatusChangedEvent
{
    public Guid EventId { get; set; } = Guid.NewGuid();
    public Guid CorrelationId { get; set; }
    public Guid OrderId { get; set; }
    public Guid CustomerId { get; set; }
    public string OldStatus { get; set; } = string.Empty;
    public string NewStatus { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public DateTime OccurredAt { get; set; } = DateTime.UtcNow;
}
