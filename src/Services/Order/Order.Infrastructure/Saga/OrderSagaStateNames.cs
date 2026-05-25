namespace Order.Infrastructure.Saga;

public static class OrderSagaStateNames
{
    public const string Submitted = nameof(Submitted);
    public const string StockReserving = nameof(StockReserving);
    public const string PaymentCreating = nameof(PaymentCreating);
    public const string PaymentPending = nameof(PaymentPending);
    public const string Cancelling = nameof(Cancelling);
}
