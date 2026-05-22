namespace EventBus.Contracts;

public class CustomerProfileChangedEvent
{
    public Guid EventId { get; set; } = Guid.NewGuid();
    public Guid CustomerId { get; set; }
    public string Email { get; set; } = string.Empty;
    public string? PhoneNumber { get; set; }
    public string FullName { get; set; } = string.Empty;
    public DateTime OccurredAt { get; set; } = DateTime.UtcNow;
}
