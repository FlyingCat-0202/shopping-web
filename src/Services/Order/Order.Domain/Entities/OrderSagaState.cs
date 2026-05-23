namespace Order.Domain.Entities;

public class OrderSagaState
{
    private static readonly Dictionary<string, string[]> AllowedTransitions = new()
    {
        [OrderSagaSteps.Started] = [OrderSagaSteps.StockReservationPending],
        [OrderSagaSteps.StockReservationPending] =
        [
            OrderSagaSteps.PaymentCreationPending,
            OrderSagaSteps.StockReleasePending,
            OrderSagaSteps.Completed,
            OrderSagaSteps.Failed
        ],
        [OrderSagaSteps.PaymentCreationPending] =
        [
            OrderSagaSteps.PaymentPending,
            OrderSagaSteps.StockReleasePending,
            OrderSagaSteps.Completed,
            OrderSagaSteps.Failed
        ],
        [OrderSagaSteps.PaymentPending] =
        [
            OrderSagaSteps.StockReleasePending,
            OrderSagaSteps.Completed,
            OrderSagaSteps.Failed
        ],
        [OrderSagaSteps.StockReleasePending] = [OrderSagaSteps.Failed],
        [OrderSagaSteps.Completed] = [],
        [OrderSagaSteps.Failed] = []
    };

    public Guid OrderId { get; set; }
    public Guid CustomerId { get; set; }
    public string CurrentStep { get; set; } = OrderSagaSteps.Started;
    public string PaymentMethod { get; set; } = string.Empty;
    public decimal TotalAmount { get; set; }
    public string? FailureReason { get; set; }
    public bool IsCompleted { get; set; }
    public int Version { get; private set; }
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
        if (CurrentStep == step)
        {
            Touch();
            return;
        }

        if (IsCompleted)
            throw new InvalidOperationException($"Order saga {OrderId} đã kết thúc ở bước {CurrentStep}.");

        if (!AllowedTransitions.TryGetValue(CurrentStep, out var allowedSteps) ||
            !allowedSteps.Contains(step))
        {
            throw new InvalidOperationException(
                $"Order saga {OrderId} không thể chuyển từ {CurrentStep} sang {step}.");
        }

        CurrentStep = step;
        Touch();
    }

    public void Complete()
    {
        MoveTo(OrderSagaSteps.Completed);
        IsCompleted = true;
        CompletedAt = DateTime.UtcNow;
        Touch();
    }

    public void Fail(string reason)
    {
        MoveTo(OrderSagaSteps.Failed);
        IsCompleted = true;
        FailureReason = reason;
        CompletedAt = DateTime.UtcNow;
        Touch();
    }

    private void Touch()
    {
        Version++;
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
