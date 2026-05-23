namespace EventBus.Contracts;

public class ReserveStockCommand
{
    public Guid CommandId { get; set; } = Guid.NewGuid();
    public Guid CorrelationId { get; set; }
    public Guid OrderId { get; set; }
    public Guid CustomerId { get; set; }
    public List<OrderItemInfo> Items { get; set; } = [];
    public DateTime RequestedAt { get; set; } = DateTime.UtcNow;
}

public class ReleaseStockCommand
{
    public Guid CommandId { get; set; } = Guid.NewGuid();
    public Guid CorrelationId { get; set; }
    public Guid OrderId { get; set; }
    public Guid CustomerId { get; set; }
    public string Reason { get; set; } = string.Empty;
    public List<OrderItemInfo> Items { get; set; } = [];
    public DateTime RequestedAt { get; set; } = DateTime.UtcNow;
}

public class StockReservedEvent
{
    public Guid EventId { get; set; } = Guid.NewGuid();
    public Guid CorrelationId { get; set; }
    public Guid OrderId { get; set; }
    public Guid CustomerId { get; set; }
    public List<ValidatedOrderItem> Items { get; set; } = [];
    public DateTime OccurredAt { get; set; } = DateTime.UtcNow;
}

public class ValidatedOrderItem
{
    public Guid ProductId { get; set; }
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
}

public class StockReservationFailedEvent
{
    public Guid EventId { get; set; } = Guid.NewGuid();
    public Guid CorrelationId { get; set; }
    public Guid OrderId { get; set; }
    public Guid CustomerId { get; set; }
    public string Reason { get; set; } = string.Empty;
    public DateTime OccurredAt { get; set; } = DateTime.UtcNow;
}

public class StockReleasedEvent
{
    public Guid EventId { get; set; } = Guid.NewGuid();
    public Guid CorrelationId { get; set; }
    public Guid OrderId { get; set; }
    public Guid CustomerId { get; set; }
    public string Reason { get; set; } = string.Empty;
    public DateTime OccurredAt { get; set; } = DateTime.UtcNow;
}
