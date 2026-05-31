using EventBus.Contracts;
using MassTransit;
using Order.API.Saga;
using Order.Domain.Enums;
using Order.Infrastructure.Saga;
using Payment.Domain.Entities;
using PaymentStatus = Payment.Domain.Enums.PaymentStatus;
using Xunit;

namespace Order.FlowTests;

public class OrderFlowTests
{
    [Fact]
    public void StateMachineExposesRealFlowEvents()
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
            typeof(PaymentRefundedEvent),
            typeof(PaymentSucceededEvent),
            typeof(PaymentTimeoutExpired),
            typeof(StockReleasedEvent),
            typeof(StockReservationFailedEvent),
            typeof(StockReservedEvent),
            typeof(StockTimeoutExpired)
        }.OrderBy(t => t.Name).ToArray();

        Assert.Equal(expectedEvents, eventTypes);
        Assert.DoesNotContain(typeof(OrderStatusChangedEvent), eventTypes);
    }

    [Fact]
    public void SagaTimeoutStateNamesMatchStateMachineProperties()
    {
        var statePropertyNames = typeof(OrderStateMachine)
            .GetProperties()
            .Where(p => p.PropertyType == typeof(State))
            .Select(p => p.Name)
            .ToHashSet(StringComparer.Ordinal);

        var expectedStateNames = new[]
        {
            OrderSagaStateNames.Submitted,
            OrderSagaStateNames.StockReserving,
            OrderSagaStateNames.PaymentCreating,
            OrderSagaStateNames.PaymentPending,
            OrderSagaStateNames.Cancelling
        };

        foreach (var stateName in expectedStateNames)
        {
            Assert.Contains(stateName, statePropertyNames);
        }
    }

    [Fact]
    public void CodProcessingOrderCanBeCancelledBeforeShipping()
    {
        var order = CreateOrder(PaymentMethodType.COD);

        order.MarkStockReserved(PriceMap(order, 55_000m));
        Assert.Equal(OrderStatus.Processing, order.Status);
        Assert.True(order.CanCancel());

        order.Cancel();
        Assert.Equal(OrderStatus.Cancelled, order.Status);
    }

    [Fact]
    public void OnlinePaidProcessingOrderCannotBeCancelledWithoutRefundFlow()
    {
        var order = CreateOrder(PaymentMethodType.MeiMei);

        order.MarkStockReserved(PriceMap(order, 100_000m));
        order.ConfirmPayment();

        Assert.Equal(OrderStatus.Processing, order.Status);
        Assert.False(order.CanCancel());
    }

    [Fact]
    public void OnlinePaymentPendingOrderCanBeCancelled()
    {
        var order = CreateOrder(PaymentMethodType.MeilyMeily);

        order.MarkStockReserved(PriceMap(order, 80_000m));
        Assert.Equal(OrderStatus.PaymentPending, order.Status);
        Assert.True(order.CanCancel());

        order.Cancel();
        Assert.Equal(OrderStatus.Cancelled, order.Status);
    }

    [Fact]
    public void StockTimeoutCancelsPendingOrder()
    {
        var order = CreateOrder(PaymentMethodType.MeiMei);

        order.CancelDueToStockFailure();

        Assert.Equal(OrderStatus.Cancelled, order.Status);
    }

    [Fact]
    public void PaymentSuccessCanProcessBeforePaymentCreatedEvent()
    {
        var order = CreateOrder(PaymentMethodType.MeilyMeily);

        order.MarkStockReserved(PriceMap(order, 100_000m));
        order.ConfirmPayment();

        Assert.Equal(OrderStatus.Processing, order.Status);
        Assert.Equal(200_000m, order.TotalAmount);
    }

    [Fact]
    public void SucceededPaymentCanBeRefundedDuringCancellation()
    {
        var payment = PaymentTransaction.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            100_000m,
            PaymentMethodType.MeiMei.ToString());

        payment.MarkSucceeded("provider-transaction");
        payment.MarkRefunded("Order was cancelled after payment succeeded.");

        Assert.Equal(PaymentStatus.Refunded, payment.Status);
    }

    [Fact]
    public void OrderShippingDeliveryAndReturnFlowKeepsValidTransitions()
    {
        var order = CreateOrder(PaymentMethodType.COD);

        order.MarkStockReserved(PriceMap(order, 55_000m));
        order.Ship();
        order.Deliver();
        order.RequestReturn();
        order.ApproveReturn();

        Assert.Equal(OrderStatus.Returned, order.Status);
        Assert.Equal(Order.Domain.Enums.PaymentStatus.Refunded, order.PaymentState);
    }

    [Fact]
    public void DeliveredOrderReturnCanBeRejectedAndRemainPaid()
    {
        var order = CreateOrder(PaymentMethodType.MeiMei);

        order.MarkStockReserved(PriceMap(order, 55_000m));
        order.ConfirmPayment();
        order.Ship();
        order.Deliver();
        order.RequestReturn();
        order.RejectReturn();

        Assert.Equal(OrderStatus.ReturnRejected, order.Status);
        Assert.Equal(Order.Domain.Enums.PaymentStatus.Paid, order.PaymentState);
    }

    private static Order.Domain.Entities.Order CreateOrder(PaymentMethodType paymentMethod)
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

    private static Dictionary<Guid, decimal> PriceMap(Order.Domain.Entities.Order order, decimal unitPrice)
        => order.Items.ToDictionary(i => i.ProductId, _ => unitPrice);
}
