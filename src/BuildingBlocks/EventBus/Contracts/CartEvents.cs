namespace EventBus.Contracts;

public class CartItemsRemovedEvent
{
    public Guid CustomerId { get; set; }
    public List<Guid> ProductIds { get; set; } = [];
}
