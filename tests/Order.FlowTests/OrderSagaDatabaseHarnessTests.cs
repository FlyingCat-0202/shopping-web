using DotNet.Testcontainers.Builders;
using EventBus.Contracts;
using MassTransit;
using MassTransit.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Order.API.Saga;
using Order.Domain.Enums;
using Order.Infrastructure.Data;
using Order.Infrastructure.Saga;
using Testcontainers.PostgreSql;
using Xunit;

namespace Order.FlowTests;

public class OrderSagaDatabaseHarnessTests
{
    [SkippableFact]
    public async Task PaymentFailedCancelsPaymentPendingOrderAndReleasesStock()
        => await WithOrderDatabase(async (connectionString, dbOptions) =>
        {
            var customerId = Guid.NewGuid();
            var productId = Guid.NewGuid();
            var orderId = await SeedOnlinePaymentPendingOrder(
                dbOptions,
                customerId,
                productId,
                OrderSagaStateNames.PaymentPending);

            await WithSagaHarness(connectionString, async (provider, harness) =>
            {
                var sagaHarness = harness.GetSagaStateMachineHarness<OrderStateMachine, OrderSagaInstance>();
                var stateMachine = provider.GetRequiredService<OrderStateMachine>();

                await harness.Bus.Publish(new PaymentFailedEvent
                {
                    CorrelationId = orderId,
                    OrderId = orderId,
                    CustomerId = customerId,
                    PaymentId = Guid.NewGuid(),
                    Reason = "Provider declined payment."
                });

                Assert.True(await harness.Published.Any<ReleaseStockCommand>());
                Assert.NotNull(await sagaHarness.Exists(orderId, stateMachine.Cancelling, TimeSpan.FromSeconds(5)));

                await using var db = new OrderDbContext(dbOptions);
                var order = await db.Orders
                    .Include(o => o.Timeline)
                    .SingleAsync(o => o.Id == orderId);

                Assert.Equal(OrderStatus.Cancelled, order.Status);
                Assert.Contains(order.Timeline, item =>
                    item.Status == OrderStatus.Cancelled.ToString() &&
                    item.Description.Contains("Provider declined payment."));
            });
        });

    [SkippableFact]
    public async Task PaymentRefundedDuringCancellingSagaReleasesStock()
        => await WithOrderDatabase(async (connectionString, dbOptions) =>
        {
            var customerId = Guid.NewGuid();
            var productId = Guid.NewGuid();
            var orderId = await SeedOnlinePaymentPendingOrder(
                dbOptions,
                customerId,
                productId,
                OrderSagaStateNames.Cancelling,
                cancelOrder: true);

            await WithSagaHarness(connectionString, async (_, harness) =>
            {
                await harness.Bus.Publish(new PaymentRefundedEvent
                {
                    CorrelationId = orderId,
                    OrderId = orderId,
                    CustomerId = customerId,
                    PaymentId = Guid.NewGuid(),
                    Amount = 150_000m,
                    PaymentMethod = PaymentMethodType.MeiMei.ToString(),
                    ProviderTransactionId = "refund-transaction",
                    Reason = "Payment refunded after customer cancellation."
                });

                Assert.True(await harness.Published.Any<ReleaseStockCommand>());

                var releaseStock = harness.Published
                    .Select<ReleaseStockCommand>()
                    .Select(x => x.Context.Message)
                    .Single(x => x.OrderId == orderId);

                Assert.Equal(customerId, releaseStock.CustomerId);
                Assert.Equal("Payment refunded after customer cancellation.", releaseStock.Reason);
                Assert.Contains(releaseStock.Items, item => item.ProductId == productId && item.Quantity == 2);
            });
        });

    [SkippableFact]
    public async Task PaymentSucceededCompletesPaymentCreatingSagaAndProcessesOrder()
        => await WithOrderDatabase(async (connectionString, dbOptions) =>
        {
            var customerId = Guid.NewGuid();
            var productId = Guid.NewGuid();
            var orderId = await SeedOnlinePaymentCreatingOrder(dbOptions, customerId, productId);

            await WithSagaHarness(connectionString, async (_, harness) =>
            {
                var sagaHarness = harness.GetSagaStateMachineHarness<OrderStateMachine, OrderSagaInstance>();

                await harness.Bus.Publish(CreatePaymentSucceededEvent(orderId, customerId));

                Assert.True(await sagaHarness.Consumed.Any<PaymentSucceededEvent>());
                Assert.True(await harness.Published.Any<RemoveCartItemsCommand>());
                Assert.Null(await sagaHarness.NotExists(orderId, TimeSpan.FromSeconds(5)));

                await using var db = new OrderDbContext(dbOptions);
                var processedOrder = await db.Orders
                    .Include(o => o.Timeline)
                    .SingleAsync(o => o.Id == orderId);

                Assert.Equal(OrderStatus.Processing, processedOrder.Status);
                Assert.Contains(processedOrder.Timeline, item =>
                    item.Status == OrderStatus.Processing.ToString() &&
                    item.Description == "Payment succeeded.");
            });
        });

    [SkippableFact]
    public async Task OnlinePaymentSagaProcessesSuccessfulPaymentEndToEnd()
        => await WithOrderDatabase(async (connectionString, dbOptions) =>
        {
            var customerId = Guid.NewGuid();
            var productId = Guid.NewGuid();
            var orderId = await SeedPendingOnlineOrder(dbOptions, customerId, productId);

            await WithSagaHarness(connectionString, async (provider, harness) =>
            {
                var stateMachine = provider.GetRequiredService<OrderStateMachine>();
                var sagaHarness = harness.GetSagaStateMachineHarness<OrderStateMachine, OrderSagaInstance>();

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

                Assert.NotNull(await sagaHarness.Exists(orderId, stateMachine.StockReserving, TimeSpan.FromSeconds(5)));
                Assert.True(await harness.Published.Any<ReserveStockCommand>());

                await harness.Bus.Publish(CreateStockReservedEvent(orderId, customerId, productId));

                Assert.NotNull(await sagaHarness.Exists(orderId, stateMachine.PaymentCreating, TimeSpan.FromSeconds(5)));
                Assert.True(await harness.Published.Any<CreatePaymentCommand>());

                var createPayment = harness.Published
                    .Select<CreatePaymentCommand>()
                    .Select(x => x.Context.Message)
                    .Single(x => x.OrderId == orderId);

                Assert.Equal(150_000m, createPayment.Amount);
                Assert.Equal(PaymentMethodType.MeiMei.ToString(), createPayment.PaymentMethod);
                Assert.True(await harness.Consumed.Any<CreatePaymentCommand>());

                Assert.True(await harness.Published.Any<RemoveCartItemsCommand>());
                Assert.Null(await sagaHarness.NotExists(orderId, TimeSpan.FromSeconds(5)));

                await using var db = new OrderDbContext(dbOptions);
                var order = await db.Orders
                    .Include(o => o.Items)
                    .Include(o => o.Timeline)
                    .SingleAsync(o => o.Id == orderId);

                Assert.Equal(OrderStatus.Processing, order.Status);
                Assert.Equal(150_000m, order.TotalAmount);
                Assert.Contains(order.Items, item =>
                    item.ProductId == productId &&
                    item.ProductName == "Trail Jacket" &&
                    item.UnitPrice == 75_000m);
                Assert.Contains(order.Timeline, item =>
                    item.Status == OrderStatus.PaymentPending.ToString() &&
                    item.Source == "Saga");
                Assert.Contains(order.Timeline, item =>
                    item.Status == OrderStatus.Processing.ToString() &&
                    item.Description == "Payment succeeded.");
            });
        });

    [SkippableFact]
    public async Task DuplicateStockReservedEventDoesNotCreateDuplicatePayment()
        => await WithOrderDatabase(async (connectionString, dbOptions) =>
        {
            var customerId = Guid.NewGuid();
            var productId = Guid.NewGuid();
            var orderId = await SeedPendingOnlineOrderWithSaga(
                dbOptions,
                customerId,
                productId,
                OrderSagaStateNames.StockReserving);

            await WithSagaHarness(connectionString, async (provider, harness) =>
            {
                var stateMachine = provider.GetRequiredService<OrderStateMachine>();
                var sagaHarness = harness.GetSagaStateMachineHarness<OrderStateMachine, OrderSagaInstance>();

                await harness.Bus.Publish(CreateStockReservedEvent(orderId, customerId, productId));
                Assert.NotNull(await sagaHarness.Exists(orderId, stateMachine.PaymentCreating, TimeSpan.FromSeconds(5)));

                await harness.Bus.Publish(CreateStockReservedEvent(orderId, customerId, productId));
                Assert.NotNull(await sagaHarness.Exists(orderId, stateMachine.PaymentCreating, TimeSpan.FromSeconds(5)));

                var createPaymentCommands = harness.Published
                    .Select<CreatePaymentCommand>()
                    .Select(x => x.Context.Message)
                    .Where(x => x.OrderId == orderId)
                    .ToList();

                Assert.Single(createPaymentCommands);

                await using var db = new OrderDbContext(dbOptions);
                var order = await db.Orders
                    .Include(o => o.Timeline)
                    .SingleAsync(o => o.Id == orderId);

                Assert.Equal(OrderStatus.PaymentPending, order.Status);
                Assert.Single(order.Timeline, item => item.Status == OrderStatus.PaymentPending.ToString());
            }, addPaymentConsumer: false);
        });

    [SkippableFact]
    public async Task DuplicatePaymentSucceededEventDoesNotProcessOrderTwice()
        => await WithOrderDatabase(async (connectionString, dbOptions) =>
        {
            var customerId = Guid.NewGuid();
            var productId = Guid.NewGuid();
            var orderId = await SeedOnlinePaymentPendingOrder(
                dbOptions,
                customerId,
                productId,
                OrderSagaStateNames.PaymentPending);

            await WithSagaHarness(connectionString, async (_, harness) =>
            {
                var sagaHarness = harness.GetSagaStateMachineHarness<OrderStateMachine, OrderSagaInstance>();

                await harness.Bus.Publish(CreatePaymentSucceededEvent(orderId, customerId));
                Assert.Null(await sagaHarness.NotExists(orderId, TimeSpan.FromSeconds(5)));

                await harness.Bus.Publish(CreatePaymentSucceededEvent(orderId, customerId));
                await Task.Delay(TimeSpan.FromMilliseconds(200));

                var removeCartCommands = harness.Published
                    .Select<RemoveCartItemsCommand>()
                    .Select(x => x.Context.Message)
                    .Where(x => x.OrderId == orderId)
                    .ToList();

                Assert.Single(removeCartCommands);

                await using var db = new OrderDbContext(dbOptions);
                var order = await db.Orders
                    .Include(o => o.Timeline)
                    .SingleAsync(o => o.Id == orderId);

                Assert.Equal(OrderStatus.Processing, order.Status);
                Assert.Single(order.Timeline, item =>
                    item.Status == OrderStatus.Processing.ToString() &&
                    item.Description == "Payment succeeded.");
            });
        });

    [SkippableFact]
    public async Task LatePaymentSucceededAfterCancellationPublishesCancelPaymentCommand()
        => await WithOrderDatabase(async (connectionString, dbOptions) =>
        {
            var customerId = Guid.NewGuid();
            var productId = Guid.NewGuid();
            var orderId = await SeedOnlinePaymentPendingOrder(
                dbOptions,
                customerId,
                productId,
                OrderSagaStateNames.Cancelling,
                cancelOrder: true);

            await WithSagaHarness(connectionString, async (_, harness) =>
            {
                await harness.Bus.Publish(CreatePaymentSucceededEvent(orderId, customerId));

                Assert.True(await harness.Published.Any<CancelPaymentCommand>());

                var cancelPayment = harness.Published
                    .Select<CancelPaymentCommand>()
                    .Select(x => x.Context.Message)
                    .Single(x => x.OrderId == orderId);

                Assert.Equal(orderId, cancelPayment.CorrelationId);
                Assert.Equal(customerId, cancelPayment.CustomerId);
                Assert.Equal(150_000m, cancelPayment.Amount);
                Assert.Equal(PaymentMethodType.MeiMei.ToString(), cancelPayment.PaymentMethod);
                Assert.Equal("Customer cancelled order.", cancelPayment.Reason);

                await using var db = new OrderDbContext(dbOptions);
                var order = await db.Orders.SingleAsync(o => o.Id == orderId);
                Assert.Equal(OrderStatus.Cancelled, order.Status);
            });
        });

    private static async Task WithOrderDatabase(Func<string, DbContextOptions<OrderDbContext>, Task> test)
    {
        Skip.IfNot(IsDockerAvailable(), "Docker is required for Order saga database integration tests.");

        await using var postgres = new PostgreSqlBuilder()
            .WithImage("postgres:17-alpine")
            .WithDatabase("order-db")
            .WithUsername("postgres")
            .WithPassword("postgres")
            .Build();

        await postgres.StartAsync();

        var dbOptions = CreateDbOptions(postgres.GetConnectionString());
        await using (var setupDb = new OrderDbContext(dbOptions))
        {
            await setupDb.Database.MigrateAsync();
        }

        await test(postgres.GetConnectionString(), dbOptions);
    }

    private static async Task WithSagaHarness(
        string connectionString,
        Func<IServiceProvider, ITestHarness, Task> test,
        bool addPaymentConsumer = true)
    {
        var services = CreateSagaHarnessServices(connectionString, addPaymentConsumer);
        await using var provider = services.BuildServiceProvider(true);
        var harness = provider.GetRequiredService<ITestHarness>();

        await harness.Start();
        try
        {
            await test(provider, harness);
        }
        finally
        {
            await harness.Stop();
        }
    }

    private static DbContextOptions<OrderDbContext> CreateDbOptions(string connectionString)
    {
        var builder = new DbContextOptionsBuilder<OrderDbContext>();
        ConfigureNpgsql(builder, connectionString);
        return builder.Options;
    }

    private static void ConfigureNpgsql(DbContextOptionsBuilder builder, string connectionString)
        => builder.UseNpgsql(connectionString, npgsql =>
        {
            npgsql.MigrationsHistoryTable("__EFMigrationsHistory_Order", "order");
        });

    private static ServiceCollection CreateSagaHarnessServices(string connectionString, bool addPaymentConsumer)
    {
        var services = new ServiceCollection();
        services.AddDbContext<OrderDbContext>(options => ConfigureNpgsql(options, connectionString));
        services.AddMassTransitTestHarness(cfg =>
        {
            cfg.SetTestTimeouts(TimeSpan.FromSeconds(12), TimeSpan.FromSeconds(1));
            if (addPaymentConsumer)
                cfg.AddConsumer<SucceedingPaymentConsumer>();

            cfg.AddSagaStateMachine<OrderStateMachine, OrderSagaInstance>()
                .EntityFrameworkRepository(r =>
                {
                    r.ExistingDbContext<OrderDbContext>();
                    r.UsePostgres();
                });
        });

        return services;
    }

    private static async Task<Guid> SeedPendingOnlineOrder(
        DbContextOptions<OrderDbContext> dbOptions,
        Guid customerId,
        Guid productId)
    {
        await using var db = new OrderDbContext(dbOptions);
        var order = CreatePendingOnlineOrder(customerId, productId);

        db.Orders.Add(order);
        await db.SaveChangesAsync();
        return order.Id;
    }

    private static async Task<Guid> SeedPendingOnlineOrderWithSaga(
        DbContextOptions<OrderDbContext> dbOptions,
        Guid customerId,
        Guid productId,
        string sagaState)
    {
        await using var db = new OrderDbContext(dbOptions);
        var order = CreatePendingOnlineOrder(customerId, productId);

        db.Orders.Add(order);
        db.OrderSagaInstances.Add(new OrderSagaInstance
        {
            CorrelationId = order.Id,
            CurrentState = sagaState,
            CustomerId = customerId,
            PaymentMethod = PaymentMethodType.MeiMei.ToString(),
            TotalAmount = 0m,
            IsCOD = false,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });

        await db.SaveChangesAsync();
        return order.Id;
    }

    private static async Task<Guid> SeedOnlinePaymentCreatingOrder(
        DbContextOptions<OrderDbContext> dbOptions,
        Guid customerId,
        Guid productId)
    {
        await using var db = new OrderDbContext(dbOptions);
        var order = CreateStockReservedOnlineOrder(customerId, productId);

        db.Orders.Add(order);
        db.OrderSagaInstances.Add(new OrderSagaInstance
        {
            CorrelationId = order.Id,
            CurrentState = OrderSagaStateNames.PaymentCreating,
            CustomerId = customerId,
            PaymentMethod = PaymentMethodType.MeiMei.ToString(),
            TotalAmount = 150_000m,
            IsCOD = false,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            StockReservedAt = DateTime.UtcNow
        });

        await db.SaveChangesAsync();
        return order.Id;
    }

    private static async Task<Guid> SeedOnlinePaymentPendingOrder(
        DbContextOptions<OrderDbContext> dbOptions,
        Guid customerId,
        Guid productId,
        string sagaState,
        bool cancelOrder = false)
    {
        await using var db = new OrderDbContext(dbOptions);
        var order = CreateStockReservedOnlineOrder(customerId, productId);

        if (cancelOrder)
            order.Cancel();

        db.Orders.Add(order);
        db.OrderSagaInstances.Add(new OrderSagaInstance
        {
            CorrelationId = order.Id,
            CurrentState = sagaState,
            CustomerId = customerId,
            PaymentMethod = PaymentMethodType.MeiMei.ToString(),
            TotalAmount = 150_000m,
            IsCOD = false,
            FailureReason = cancelOrder ? "Customer cancelled order." : null,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            StockReservedAt = DateTime.UtcNow,
            PaymentCreatedAt = DateTime.UtcNow
        });

        await db.SaveChangesAsync();
        return order.Id;
    }

    private static Order.Domain.Entities.Order CreatePendingOnlineOrder(Guid customerId, Guid productId)
    {
        var order = Order.Domain.Entities.Order.Create(
            customerId,
            PaymentMethodType.MeiMei,
            "Test Customer",
            "0900000000",
            "Test address");

        order.AddOrderItem(productId, 0m, 2);
        return order;
    }

    private static Order.Domain.Entities.Order CreateStockReservedOnlineOrder(Guid customerId, Guid productId)
    {
        var order = CreatePendingOnlineOrder(customerId, productId);
        order.MarkStockReserved(new Dictionary<Guid, decimal> { [productId] = 75_000m });
        return order;
    }

    private static StockReservedEvent CreateStockReservedEvent(Guid orderId, Guid customerId, Guid productId)
        => new()
        {
            CorrelationId = orderId,
            OrderId = orderId,
            CustomerId = customerId,
            Items =
            [
                new ValidatedOrderItem
                {
                    ProductId = productId,
                    ProductName = "Trail Jacket",
                    ProductImageUrl = "https://cdn.test/trail-jacket.png",
                    Quantity = 2,
                    UnitPrice = 75_000m
                }
            ]
        };

    private static PaymentSucceededEvent CreatePaymentSucceededEvent(Guid orderId, Guid customerId)
        => new()
        {
            CorrelationId = orderId,
            OrderId = orderId,
            CustomerId = customerId,
            PaymentId = Guid.NewGuid(),
            Amount = 150_000m,
            PaymentMethod = PaymentMethodType.MeiMei.ToString(),
            ProviderTransactionId = "provider-transaction"
        };

    private sealed class SucceedingPaymentConsumer : IConsumer<CreatePaymentCommand>
    {
        public Task Consume(ConsumeContext<CreatePaymentCommand> context)
            => context.Publish(new PaymentSucceededEvent
            {
                CorrelationId = context.Message.CorrelationId,
                OrderId = context.Message.OrderId,
                CustomerId = context.Message.CustomerId,
                PaymentId = Guid.NewGuid(),
                Amount = context.Message.Amount,
                PaymentMethod = context.Message.PaymentMethod,
                ProviderTransactionId = "provider-transaction"
            });
    }

    private static bool IsDockerAvailable()
    {
        try
        {
            var container = new ContainerBuilder()
                .WithImage("redis:7-alpine")
                .Build();

            container.DisposeAsync().AsTask().GetAwaiter().GetResult();
            return true;
        }
        catch (DockerUnavailableException)
        {
            return false;
        }
    }
}
