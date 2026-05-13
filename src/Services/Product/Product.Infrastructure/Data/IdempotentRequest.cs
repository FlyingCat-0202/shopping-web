namespace Product.Infrastructure.Data;

public class IdempotentRequest
{
    public Guid Id { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
