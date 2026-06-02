using EventBus.Contracts;
using MassTransit;
using MassTransit.Testing;
using Microsoft.Extensions.DependencyInjection;
using Order.API.Saga;
using Order.Domain.Enums;
using Order.Infrastructure.Saga;
using Xunit;

namespace Order.FlowTests;

public class OrderSagaHarnessTests
{
    [Fact]
    public async Task OrderSubmittedStartsSagaAndPublishesReserveStockCommand()
    {
        var services = new ServiceCollection();
        services.AddMassTransitTestHarness(cfg =>
        {
            cfg.SetTestTimeouts(TimeSpan.FromSeconds(8), TimeSpan.FromSeconds(1));
            cfg.AddSagaStateMachine<OrderStateMachine, OrderSagaInstance>()
                .InMemoryRepository();
        });

        await using var provider = services.BuildServiceProvider(true);
        var harness = provider.GetRequiredService<ITestHarness>();

        await harness.Start();
        try
        {
            var orderId = Guid.NewGuid();
            var customerId = Guid.NewGuid();
            var productId = Guid.NewGuid();

            await harness.Bus.Publish(new OrderSubmittedEvent
            {
                CorrelationId = orderId,
                OrderId = orderId,
                CustomerId = customerId,
                PaymentMethod = PaymentMethodType.MeiMei.ToString(),
                Items =
                [
                    new OrderItemInfo
                    {
                        ProductId = productId,
                        Quantity = 2
                    }
                ]
            });

            Assert.True(await harness.Consumed.Any<OrderSubmittedEvent>());
            Assert.True(await harness.Published.Any<ReserveStockCommand>());

            var stateMachine = provider.GetRequiredService<OrderStateMachine>();
            var sagaHarness = harness.GetSagaStateMachineHarness<OrderStateMachine, OrderSagaInstance>();
            var sagaId = await sagaHarness.Exists(orderId, stateMachine.StockReserving, TimeSpan.FromSeconds(5));

            Assert.NotNull(sagaId);

            var reserveStock = harness.Published
                .Select<ReserveStockCommand>()
                .Select(x => x.Context.Message)
                .Single(x => x.OrderId == orderId);

            Assert.Equal(orderId, reserveStock.CorrelationId);
            Assert.Equal(customerId, reserveStock.CustomerId);
            Assert.Contains(reserveStock.Items, item => item.ProductId == productId && item.Quantity == 2);
        }
        finally
        {
            await harness.Stop();
        }
    }
}
