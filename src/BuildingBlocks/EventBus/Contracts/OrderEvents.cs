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

// ── Saga Commands ────────────────────────────────────────────────────────────

public class CancelOrderCommand
{
    public Guid CorrelationId { get; set; }
    public Guid OrderId { get; set; }
    public Guid CustomerId { get; set; }
    public string Reason { get; set; } = string.Empty;
    public DateTime RequestedAt { get; set; } = DateTime.UtcNow;
}

// ── Saga Timeout Events ──────────────────────────────────────────────────────

public class StockTimeoutExpired
{
    public Guid OrderId { get; set; }
    public DateTime ExpiredAt { get; set; } = DateTime.UtcNow;
}

public class PaymentTimeoutExpired
{
    public Guid OrderId { get; set; }
    public DateTime ExpiredAt { get; set; } = DateTime.UtcNow;
}
