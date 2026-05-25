using MassTransit;

namespace Order.Domain.Entities;

public class OrderSagaInstance : SagaStateMachineInstance
{
    public Guid CorrelationId { get; set; }
    public string CurrentState { get; set; } = string.Empty;

    // ── Saga Data ────────────────────────────────────────────────────────────

    public Guid CustomerId { get; set; }
    public string PaymentMethod { get; set; } = string.Empty;
    public decimal TotalAmount { get; set; }
    public bool IsCOD { get; set; }
    public string? FailureReason { get; set; }

    // ── Reserved for transport schedulers. Current timeout flow is DB polling
    // ── from OrderSagaTimeoutService to avoid RabbitMQ delayed-exchange setup.
    public Guid? StockTimeoutTokenId { get; set; }
    public Guid? PaymentTimeoutTokenId { get; set; }

    // ── Timestamps ───────────────────────────────────────────────────────────

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? StockReservedAt { get; set; }
    public DateTime? PaymentCreatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }

    // ── Concurrency ──────────────────────────────────────────────────────────
    public int Version { get; set; }
}
