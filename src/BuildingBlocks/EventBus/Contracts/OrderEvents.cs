namespace EventBus.Contracts;

public class OrderItemInfo
{
    public Guid ProductId { get; set; }
    public int Quantity { get; set; }
}

public class OrderCreatedEvent
{
    public Guid OrderId { get; set; }
    public Guid CustomerId { get; set; }
    public List<OrderItemInfo> Items { get; set; } = [];
}

public class OrderCancelledEvent
{
    public Guid OrderId { get; set; }
    public List<OrderItemInfo> Items { get; set; } = [];
}

public class OrderReturnedEvent
{
    public Guid OrderId { get; set; }
    public List<OrderItemInfo> Items { get; set; } = [];
}

public class OrderStatusChangedEvent
{
    public Guid EventId { get; set; } = Guid.NewGuid();
    public Guid OrderId { get; set; }
    public Guid CustomerId { get; set; }
    public string OldStatus { get; set; } = string.Empty;
    public string NewStatus { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public DateTime OccurredAt { get; set; } = DateTime.UtcNow;
}
