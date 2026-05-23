namespace Order.Domain.Entities;

public class OrderSagaState
{
    public Guid OrderId { get; set; }
    public Guid CustomerId { get; set; }
    public string CurrentStep { get; set; } = OrderSagaSteps.Started;
    public string PaymentMethod { get; set; } = string.Empty;
    public decimal TotalAmount { get; set; }
    public string? FailureReason { get; set; }
    public bool IsCompleted { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? StockReservedAt { get; set; }
    public DateTime? PaymentCreatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }

    public static OrderSagaState Start(Guid orderId, Guid customerId, string paymentMethod)
        => new()
        {
            OrderId = orderId,
            CustomerId = customerId,
            CurrentStep = OrderSagaSteps.StockReservationPending,
            PaymentMethod = paymentMethod,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

    public void MoveTo(string step)
    {
        CurrentStep = step;
        UpdatedAt = DateTime.UtcNow;
    }

    public void Complete()
    {
        IsCompleted = true;
        CurrentStep = OrderSagaSteps.Completed;
        CompletedAt = DateTime.UtcNow;
        UpdatedAt = DateTime.UtcNow;
    }

    public void Fail(string reason)
    {
        IsCompleted = true;
        CurrentStep = OrderSagaSteps.Failed;
        FailureReason = reason;
        CompletedAt = DateTime.UtcNow;
        UpdatedAt = DateTime.UtcNow;
    }
}

public static class OrderSagaSteps
{
    public const string Started = "Started";
    public const string StockReservationPending = "StockReservationPending";
    public const string PaymentCreationPending = "PaymentCreationPending";
    public const string PaymentPending = "PaymentPending";
    public const string StockReleasePending = "StockReleasePending";
    public const string Completed = "Completed";
    public const string Failed = "Failed";
}
