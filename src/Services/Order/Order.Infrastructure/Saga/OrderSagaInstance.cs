using MassTransit;

namespace Order.Infrastructure.Saga;

public class OrderSagaInstance : SagaStateMachineInstance
{
    public Guid CorrelationId { get; set; }
    public string CurrentState { get; set; } = string.Empty;

    public Guid CustomerId { get; set; }
    public string PaymentMethod { get; set; } = string.Empty;
    public decimal TotalAmount { get; set; }
    public bool IsCOD { get; set; }
    public string? FailureReason { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? StockReservedAt { get; set; }
    public DateTime? PaymentCreatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
}
