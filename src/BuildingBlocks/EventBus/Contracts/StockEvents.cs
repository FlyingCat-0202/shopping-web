namespace EventBus.Contracts;

public class StockReservedEvent
{
    public Guid OrderId { get; set; }
    public List<ValidatedOrderItem> Items { get; set; } = [];
}

public class ValidatedOrderItem
{
    public Guid ProductId { get; set; }
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
}

public class StockReservationFailedEvent
{
    public Guid OrderId { get; set; }
    public string Reason { get; set; } = string.Empty;
}
