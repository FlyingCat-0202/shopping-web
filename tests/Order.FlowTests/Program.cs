using EventBus.Contracts;
using MassTransit;
using Order.API.Saga;
using Order.Domain.Enums;

var tests = new (string Name, Action Run)[]
{
    ("State machine exposes real flow events", StateMachineExposesRealFlowEvents),
    ("COD processing order can be cancelled before shipping", CodProcessingOrderCanBeCancelled),
    ("Online paid processing order cannot be cancelled without refund flow", OnlinePaidProcessingOrderCannotBeCancelled),
    ("Online payment pending order can be cancelled", OnlinePaymentPendingOrderCanBeCancelled),
    ("Stock timeout cancels pending order", StockTimeoutCancelsPendingOrder),
    ("Payment success can process before payment-created event", PaymentSuccessCanProcessBeforePaymentCreatedEvent)
};

foreach (var test in tests)
{
    test.Run();
    Console.WriteLine($"[PASS] {test.Name}");
}

static void StateMachineExposesRealFlowEvents()
{
    var eventTypes = typeof(OrderStateMachine)
        .GetProperties()
        .Where(p => p.PropertyType.IsGenericType && p.PropertyType.GetGenericTypeDefinition() == typeof(Event<>))
        .Select(p => p.PropertyType.GetGenericArguments()[0])
        .OrderBy(t => t.Name)
        .ToArray();

    var expectedEvents = new[]
    {
        typeof(CancelOrderCommand),
        typeof(OrderSubmittedEvent),
        typeof(PaymentCreatedEvent),
        typeof(PaymentFailedEvent),
        typeof(PaymentSucceededEvent),
        typeof(PaymentTimeoutExpired),
        typeof(StockReleasedEvent),
        typeof(StockReservationFailedEvent),
        typeof(StockReservedEvent),
        typeof(StockTimeoutExpired)
    }.OrderBy(t => t.Name).ToArray();

    AssertSequenceEqual(expectedEvents, eventTypes, "OrderStateMachine event subscriptions");
    AssertFalse(eventTypes.Contains(typeof(OrderStatusChangedEvent)), "Saga must not consume OrderStatusChangedEvent");
}

static void CodProcessingOrderCanBeCancelled()
{
    var order = CreateOrder(PaymentMethodType.COD);

    order.MarkStockReserved(PriceMap(order, 55_000m));
    AssertEqual(OrderStatus.Processing, order.Status, "Order status after stock reserved");
    AssertTrue(order.CanCancel(), "COD processing order can cancel");

    order.Cancel();
    AssertEqual(OrderStatus.Cancelled, order.Status, "Order status after cancel");
}

static void OnlinePaidProcessingOrderCannotBeCancelled()
{
    var order = CreateOrder(PaymentMethodType.MeiMei);

    order.MarkStockReserved(PriceMap(order, 100_000m));
    order.ConfirmPayment();

    AssertEqual(OrderStatus.Processing, order.Status, "Order status after payment");
    AssertFalse(order.CanCancel(), "Online paid processing order can cancel");
}

static void OnlinePaymentPendingOrderCanBeCancelled()
{
    var order = CreateOrder(PaymentMethodType.MeilyMeily);

    order.MarkStockReserved(PriceMap(order, 80_000m));
    AssertEqual(OrderStatus.PaymentPending, order.Status, "Order status after stock reserved");
    AssertTrue(order.CanCancel(), "Online payment pending order can cancel");

    order.Cancel();
    AssertEqual(OrderStatus.Cancelled, order.Status, "Order status after cancel");
}

static void StockTimeoutCancelsPendingOrder()
{
    var order = CreateOrder(PaymentMethodType.MeiMei);

    order.CancelDueToStockFailure();

    AssertEqual(OrderStatus.Cancelled, order.Status, "Order status");
}

static void PaymentSuccessCanProcessBeforePaymentCreatedEvent()
{
    var order = CreateOrder(PaymentMethodType.MeilyMeily);

    order.MarkStockReserved(PriceMap(order, 100_000m));
    order.ConfirmPayment();

    AssertEqual(OrderStatus.Processing, order.Status, "Order status");
    AssertEqual(200_000m, order.TotalAmount, "Order total");
}

static Order.Domain.Entities.Order CreateOrder(PaymentMethodType paymentMethod)
{
    var order = Order.Domain.Entities.Order.Create(
        Guid.NewGuid(),
        paymentMethod,
        "Test Customer",
        "0900000000",
        "Test address");

    order.AddOrderItem(Guid.NewGuid(), 0m, 2);
    return order;
}

static Dictionary<Guid, decimal> PriceMap(Order.Domain.Entities.Order order, decimal unitPrice)
    => order.Items.ToDictionary(i => i.ProductId, _ => unitPrice);

static void AssertEqual<T>(T expected, T actual, string name)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException($"{name}: expected {expected}, actual {actual}");
}

static void AssertTrue(bool value, string name)
{
    if (!value)
        throw new InvalidOperationException($"{name}: expected true");
}

static void AssertFalse(bool value, string name)
{
    if (value)
        throw new InvalidOperationException($"{name}: expected false");
}

static void AssertSequenceEqual<T>(IReadOnlyList<T> expected, IReadOnlyList<T> actual, string name)
{
    if (expected.Count != actual.Count || expected.Where((item, index) => !EqualityComparer<T>.Default.Equals(item, actual[index])).Any())
    {
        throw new InvalidOperationException(
            $"{name}: expected [{string.Join(", ", expected)}], actual [{string.Join(", ", actual)}]");
    }
}
