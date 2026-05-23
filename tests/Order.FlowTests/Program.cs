using EventBus.Contracts;
using MassTransit;
using Order.API.IntegrationEvents.Consumer;
using Order.Domain.Entities;
using Order.Domain.Enums;

var tests = new (string Name, Action Run)[]
{
    ("Saga only consumes real flow events", SagaOnlyConsumesRealFlowEvents),
    ("Stock fail cancels order", StockFailCancelsOrder),
    ("Online payment success completes order", OnlinePaymentSuccessCompletesOrder),
    ("Online payment fail releases stock", OnlinePaymentFailReleasesStock),
    ("COD success completes without payment", CodSuccessCompletesWithoutPayment)
};

foreach (var test in tests)
{
    test.Run();
    Console.WriteLine($"[PASS] {test.Name}");
}

static void SagaOnlyConsumesRealFlowEvents()
{
    var consumerInterfaces = typeof(OrderSagaConsumer)
        .GetInterfaces()
        .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IConsumer<>))
        .Select(i => i.GetGenericArguments()[0])
        .OrderBy(t => t.Name)
        .ToArray();

    var expectedEvents = new[]
    {
        typeof(OrderSubmittedEvent),
        typeof(PaymentCreatedEvent),
        typeof(PaymentFailedEvent),
        typeof(PaymentSucceededEvent),
        typeof(StockReleasedEvent),
        typeof(StockReservationFailedEvent),
        typeof(StockReservedEvent)
    }.OrderBy(t => t.Name).ToArray();

    AssertSequenceEqual(expectedEvents, consumerInterfaces, "OrderSagaConsumer event subscriptions");
    AssertFalse(consumerInterfaces.Contains(typeof(OrderStatusChangedEvent)), "Saga must not consume OrderStatusChangedEvent");
}

static void StockFailCancelsOrder()
{
    var order = CreateOrder(PaymentMethodType.MeiMei);
    var saga = StartSaga(order);

    saga.MoveTo(OrderSagaSteps.StockReservationPending);
    order.CancelDueToStockFailure();
    saga.Fail("Out of stock");

    AssertEqual(OrderStatus.Cancelled, order.Status, "Order status");
    AssertEqual(OrderSagaSteps.Failed, saga.CurrentStep, "Saga step");
    AssertTrue(saga.IsCompleted, "Saga completed");
}

static void OnlinePaymentSuccessCompletesOrder()
{
    var order = CreateOrder(PaymentMethodType.MeiMei);
    var saga = StartSaga(order);

    saga.MoveTo(OrderSagaSteps.StockReservationPending);
    order.MarkStockReserved(PriceMap(order, 120_000m));
    saga.StockReservedAt = DateTime.UtcNow;
    saga.TotalAmount = order.TotalAmount;
    saga.MoveTo(OrderSagaSteps.PaymentCreationPending);

    saga.PaymentCreatedAt = DateTime.UtcNow;
    saga.MoveTo(OrderSagaSteps.PaymentPending);

    order.ConfirmPayment();
    saga.Complete();

    AssertEqual(OrderStatus.Processing, order.Status, "Order status");
    AssertEqual(240_000m, order.TotalAmount, "Order total");
    AssertEqual(OrderSagaSteps.Completed, saga.CurrentStep, "Saga step");
    AssertTrue(saga.IsCompleted, "Saga completed");
}

static void OnlinePaymentFailReleasesStock()
{
    var order = CreateOrder(PaymentMethodType.MeilyMeily);
    var saga = StartSaga(order);

    saga.MoveTo(OrderSagaSteps.StockReservationPending);
    order.MarkStockReserved(PriceMap(order, 80_000m));
    saga.StockReservedAt = DateTime.UtcNow;
    saga.MoveTo(OrderSagaSteps.PaymentCreationPending);

    saga.PaymentCreatedAt = DateTime.UtcNow;
    saga.MoveTo(OrderSagaSteps.PaymentPending);

    order.CancelDueToPaymentFailure();
    saga.MoveTo(OrderSagaSteps.StockReleasePending);

    saga.Fail("Payment failed and stock released");

    AssertEqual(OrderStatus.Cancelled, order.Status, "Order status");
    AssertEqual(OrderSagaSteps.Failed, saga.CurrentStep, "Saga step");
    AssertTrue(saga.IsCompleted, "Saga completed");
}

static void CodSuccessCompletesWithoutPayment()
{
    var order = CreateOrder(PaymentMethodType.COD);
    var saga = StartSaga(order);

    saga.MoveTo(OrderSagaSteps.StockReservationPending);
    order.MarkStockReserved(PriceMap(order, 55_000m));
    saga.StockReservedAt = DateTime.UtcNow;
    saga.TotalAmount = order.TotalAmount;
    saga.Complete();

    AssertEqual(OrderStatus.Processing, order.Status, "Order status");
    AssertEqual(110_000m, order.TotalAmount, "Order total");
    AssertEqual(OrderSagaSteps.Completed, saga.CurrentStep, "Saga step");
    AssertTrue(saga.IsCompleted, "Saga completed");
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

static OrderSagaState StartSaga(Order.Domain.Entities.Order order)
    => OrderSagaState.Start(order.Id, order.CustomerId, order.PaymentMethod.ToString());

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
