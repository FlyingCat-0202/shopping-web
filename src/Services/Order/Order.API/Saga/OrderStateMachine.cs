using EventBus.Contracts;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Order.Domain.Entities;
using Order.Domain.Enums;
using Order.Infrastructure.Data;
using Order.Infrastructure.Saga;

namespace Order.API.Saga;

public class OrderStateMachine : MassTransitStateMachine<OrderSagaInstance>
{
    // ── States ───────────────────────────────────────────────────────────────
    public State Submitted { get; private set; } = null!;
    public State StockReserving { get; private set; } = null!;
    public State PaymentCreating { get; private set; } = null!;
    public State PaymentPending { get; private set; } = null!;
    public State Cancelling { get; private set; } = null!;

    // ── Events ───────────────────────────────────────────────────────────────
    public Event<OrderSubmittedEvent> OrderSubmitted { get; private set; } = null!;
    public Event<StockReservedEvent> StockReserved { get; private set; } = null!;
    public Event<StockReservationFailedEvent> StockReservationFailed { get; private set; } = null!;
    public Event<PaymentCreatedEvent> PaymentCreated { get; private set; } = null!;
    public Event<PaymentSucceededEvent> PaymentSucceeded { get; private set; } = null!;
    public Event<PaymentRefundedEvent> PaymentRefunded { get; private set; } = null!;
    public Event<PaymentFailedEvent> PaymentFailed { get; private set; } = null!;
    public Event<StockReleasedEvent> StockReleased { get; private set; } = null!;
    public Event<CancelOrderCommand> CancelRequested { get; private set; } = null!;

    public Event<StockTimeoutExpired> StockTimedOut { get; private set; } = null!;
    public Event<PaymentTimeoutExpired> PaymentTimedOut { get; private set; } = null!;

    public OrderStateMachine()
    {
        InstanceState(x => x.CurrentState);

        Event(() => OrderSubmitted, x => x.CorrelateById(ctx => ctx.Message.OrderId));
        Event(() => StockReserved, x => x.CorrelateById(ctx => ctx.Message.OrderId));
        Event(() => StockReservationFailed, x => x.CorrelateById(ctx => ctx.Message.OrderId));
        Event(() => PaymentCreated, x => x.CorrelateById(ctx => ctx.Message.OrderId));
        Event(() => PaymentSucceeded, x => x.CorrelateById(ctx => ctx.Message.OrderId));
        Event(() => PaymentRefunded, x => x.CorrelateById(ctx => ctx.Message.OrderId));
        Event(() => PaymentFailed, x => x.CorrelateById(ctx => ctx.Message.OrderId));
        Event(() => StockReleased, x => x.CorrelateById(ctx => ctx.Message.OrderId));
        Event(() => CancelRequested, x => x.CorrelateById(ctx => ctx.Message.OrderId));
        Event(() => StockTimedOut, x => x.CorrelateById(ctx => ctx.Message.OrderId));
        Event(() => PaymentTimedOut, x => x.CorrelateById(ctx => ctx.Message.OrderId));

        Initially(
            When(OrderSubmitted)
                .Then(ctx =>
                {
                    ctx.Saga.CustomerId = ctx.Message.CustomerId;
                    ctx.Saga.PaymentMethod = ctx.Message.PaymentMethod;
                    ctx.Saga.IsCOD = string.Equals(
                        ctx.Message.PaymentMethod,
                        nameof(PaymentMethodType.COD),
                        StringComparison.OrdinalIgnoreCase);
                    ctx.Saga.CreatedAt = DateTime.UtcNow;
                    ctx.Saga.UpdatedAt = DateTime.UtcNow;
                })
                .PublishAsync(ctx => ctx.Init<ReserveStockCommand>(new
                {
                    CorrelationId = ctx.Saga.CorrelationId,
                    OrderId = ctx.Saga.CorrelationId,
                    ctx.Saga.CustomerId,
                    ctx.Message.Items
                }))
                .TransitionTo(StockReserving)
        );

        During(StockReserving,
            When(OrderSubmitted).Then(_ => { }),

            When(StockReserved)
                .Then(ctx =>
                {
                    ctx.Saga.StockReservedAt = DateTime.UtcNow;
                    ctx.Saga.UpdatedAt = DateTime.UtcNow;
                })
                .Activity(x => x.OfType<StockReservedActivity>())
                .If(ctx => ctx.Saga.IsCOD,
                    cod => cod
                        .Then(ctx =>
                        {
                            ctx.Saga.CompletedAt = DateTime.UtcNow;
                            ctx.Saga.UpdatedAt = DateTime.UtcNow;
                        })
                        .PublishAsync(ctx => ctx.Init<RemoveCartItemsCommand>(new
                        {
                            CorrelationId = ctx.Saga.CorrelationId,
                            OrderId = ctx.Saga.CorrelationId,
                            ctx.Saga.CustomerId,
                            ProductIds = ctx.Message.Items.Select(i => i.ProductId).ToList()
                        }))
                        .Finalize())
                .If(ctx => !ctx.Saga.IsCOD,
                    online => online
                        .PublishAsync(ctx => ctx.Init<CreatePaymentCommand>(new
                        {
                            CorrelationId = ctx.Saga.CorrelationId,
                            OrderId = ctx.Saga.CorrelationId,
                            ctx.Saga.CustomerId,
                            Amount = ctx.Saga.TotalAmount,
                            ctx.Saga.PaymentMethod
                        }))
                        .TransitionTo(PaymentCreating)),

            When(StockReservationFailed)
                .Then(ctx =>
                {
                    ctx.Saga.FailureReason = ctx.Message.Reason;
                    ctx.Saga.UpdatedAt = DateTime.UtcNow;
                })
                .Activity(x => x.OfType<StockFailedActivity>())
                .Finalize(),

            When(StockTimedOut)
                .Then(ctx =>
                {
                    ctx.Saga.FailureReason = "Order timeout while waiting for stock reservation.";
                    ctx.Saga.UpdatedAt = DateTime.UtcNow;
                })
                .Activity(x => x.OfType<TimeoutCancelActivity>())
                .TransitionTo(Cancelling),

            When(CancelRequested)
                .Then(ctx =>
                {
                    ctx.Saga.FailureReason = ctx.Message.Reason;
                    ctx.Saga.UpdatedAt = DateTime.UtcNow;
                })
                .Activity(x => x.OfType<CancelOrderActivity>())
                .TransitionTo(Cancelling)
        );

        During(PaymentCreating,
            When(OrderSubmitted).Then(_ => { }),
            When(StockReserved).Then(_ => { }),

            When(PaymentCreated)
                .Then(ctx =>
                {
                    ctx.Saga.PaymentCreatedAt = DateTime.UtcNow;
                    ctx.Saga.UpdatedAt = DateTime.UtcNow;
                })
                .TransitionTo(PaymentPending),

            When(PaymentSucceeded)
                .Then(ctx =>
                {
                    ctx.Saga.CompletedAt = DateTime.UtcNow;
                    ctx.Saga.UpdatedAt = DateTime.UtcNow;
                })
                .Activity(x => x.OfType<PaymentSucceededActivity>())
                .Finalize(),

            When(PaymentFailed)
                .Then(ctx =>
                {
                    ctx.Saga.FailureReason = $"Payment creation failed: {ctx.Message.Reason}";
                    ctx.Saga.UpdatedAt = DateTime.UtcNow;
                })
                .Activity(x => x.OfType<PaymentFailedCancelActivity>())
                .TransitionTo(Cancelling),

            When(PaymentTimedOut)
                .Then(ctx =>
                {
                    ctx.Saga.FailureReason = "Payment creation timeout.";
                    ctx.Saga.UpdatedAt = DateTime.UtcNow;
                })
                .Activity(x => x.OfType<PaymentTimeoutCancelActivity>())
                .TransitionTo(Cancelling),

            When(CancelRequested)
                .Then(ctx =>
                {
                    ctx.Saga.FailureReason = ctx.Message.Reason;
                    ctx.Saga.UpdatedAt = DateTime.UtcNow;
                })
                .Activity(x => x.OfType<CancelWithCompensationActivity>())
                .TransitionTo(Cancelling)
        );

        During(PaymentPending,
            When(OrderSubmitted).Then(_ => { }),
            When(StockReserved).Then(_ => { }),
            When(PaymentCreated).Then(_ => { }),

            When(PaymentSucceeded)
                .Then(ctx =>
                {
                    ctx.Saga.CompletedAt = DateTime.UtcNow;
                    ctx.Saga.UpdatedAt = DateTime.UtcNow;
                })
                .Activity(x => x.OfType<PaymentSucceededActivity>())
                .Finalize(),

            When(PaymentFailed)
                .Then(ctx =>
                {
                    ctx.Saga.FailureReason = $"Payment failed: {ctx.Message.Reason}";
                    ctx.Saga.UpdatedAt = DateTime.UtcNow;
                })
                .Activity(x => x.OfType<PaymentFailedCancelActivity>())
                .TransitionTo(Cancelling),

            When(PaymentTimedOut)
                .Then(ctx =>
                {
                    ctx.Saga.FailureReason = "Payment timeout.";
                    ctx.Saga.UpdatedAt = DateTime.UtcNow;
                })
                .Activity(x => x.OfType<PaymentTimeoutCancelActivity>())
                .TransitionTo(Cancelling),

            When(CancelRequested)
                .Then(ctx =>
                {
                    ctx.Saga.FailureReason = ctx.Message.Reason;
                    ctx.Saga.UpdatedAt = DateTime.UtcNow;
                })
                .Activity(x => x.OfType<CancelWithCompensationActivity>())
                .TransitionTo(Cancelling)
        );

        During(Cancelling,
            When(OrderSubmitted).Then(_ => { }),

            When(StockReserved)
                .Activity(x => x.OfType<LateStockReservedActivity>()),

            When(StockReleased)
                .Then(ctx =>
                {
                    ctx.Saga.CompletedAt = DateTime.UtcNow;
                    ctx.Saga.UpdatedAt = DateTime.UtcNow;
                })
                .Finalize(),

            When(CancelRequested).Then(_ => { }),
            When(StockTimedOut).Then(_ => { }),
            When(PaymentTimedOut).Then(_ => { }),
            When(PaymentSucceeded)
                .Activity(x => x.OfType<LatePaymentSucceededActivity>()),
            When(PaymentFailed)
                .Activity(x => x.OfType<ReleaseStockAfterPaymentCancelledActivity>()),
            When(PaymentRefunded)
                .Activity(x => x.OfType<ReleaseStockAfterPaymentRefundedActivity>()),
            When(StockReservationFailed)
                .Then(ctx =>
                {
                    ctx.Saga.CompletedAt = DateTime.UtcNow;
                    ctx.Saga.UpdatedAt = DateTime.UtcNow;
                })
                .Finalize()
        );

        SetCompletedWhenFinalized();
    }

}

public class StockReservedActivity(OrderDbContext db) :
    IStateMachineActivity<OrderSagaInstance, StockReservedEvent>
{
    public async Task Execute(BehaviorContext<OrderSagaInstance, StockReservedEvent> context,
        IBehavior<OrderSagaInstance, StockReservedEvent> next)
    {
        var order = await db.Orders.Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.Id == context.Saga.CorrelationId);

        if (order is not null && order.Status == OrderStatus.Pending)
        {
            var oldStatus = order.Status;
            var snapshots = context.Message.Items.Select(i => new OrderItemSnapshot(
                i.ProductId, i.ProductName, i.ProductImageUrl, i.UnitPrice));
            order.MarkStockReserved(snapshots);
            context.Saga.TotalAmount = order.TotalAmount;

            SagaActivityHelper.AddTimelineEvent(db, order, order.Status, $"Order {order.Status}",
                "Kho hàng đã được giữ thành công.", "Saga");

            await SagaActivityHelper.PublishStatusChanged(context, order, oldStatus,
                order.IsOnlinePayment()
                    ? "Stock reserved, waiting for online payment."
                    : "Stock reserved for COD order.");
            await db.SaveChangesAsync();
        }

        await next.Execute(context);
    }

    public Task Faulted<TException>(BehaviorExceptionContext<OrderSagaInstance, StockReservedEvent, TException> context,
        IBehavior<OrderSagaInstance, StockReservedEvent> next)
        where TException : Exception => next.Faulted(context);

    public void Probe(ProbeContext context) => context.CreateScope("stock-reserved-activity");
    public void Accept(StateMachineVisitor visitor) => visitor.Visit(this);
}

public class StockFailedActivity(OrderDbContext db) :
    IStateMachineActivity<OrderSagaInstance, StockReservationFailedEvent>
{
    public async Task Execute(BehaviorContext<OrderSagaInstance, StockReservationFailedEvent> context,
        IBehavior<OrderSagaInstance, StockReservationFailedEvent> next)
    {
        var order = await db.Orders.FirstOrDefaultAsync(o => o.Id == context.Saga.CorrelationId);
        if (order is not null && order.Status == OrderStatus.Pending)
        {
            var oldStatus = order.Status;
            order.CancelDueToStockFailure();
            SagaActivityHelper.AddTimelineEvent(db, order, order.Status, $"Order {order.Status}",
                $"Stock reservation failed: {context.Message.Reason}", "Saga");
            await SagaActivityHelper.PublishStatusChanged(context, order, oldStatus,
                $"Stock reservation failed: {context.Message.Reason}");
            await db.SaveChangesAsync();
        }

        context.Saga.CompletedAt = DateTime.UtcNow;
        await next.Execute(context);
    }

    public Task Faulted<TException>(BehaviorExceptionContext<OrderSagaInstance, StockReservationFailedEvent, TException> context,
        IBehavior<OrderSagaInstance, StockReservationFailedEvent> next)
        where TException : Exception => next.Faulted(context);

    public void Probe(ProbeContext context) => context.CreateScope("stock-failed-activity");
    public void Accept(StateMachineVisitor visitor) => visitor.Visit(this);
}

public class PaymentSucceededActivity(OrderDbContext db) :
    IStateMachineActivity<OrderSagaInstance, PaymentSucceededEvent>
{
    public async Task Execute(BehaviorContext<OrderSagaInstance, PaymentSucceededEvent> context,
        IBehavior<OrderSagaInstance, PaymentSucceededEvent> next)
    {
        var order = await db.Orders.Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.Id == context.Saga.CorrelationId);

        if (order is not null && order.Status == OrderStatus.PaymentPending)
        {
            var oldStatus = order.Status;
            order.ConfirmPayment();
            SagaActivityHelper.AddTimelineEvent(db, order, order.Status, $"Order {order.Status}",
                "Payment succeeded.", "Saga");
            await SagaActivityHelper.PublishStatusChanged(context, order, oldStatus, "Payment succeeded.");

            await context.Publish(new RemoveCartItemsCommand
            {
                CorrelationId = order.Id,
                OrderId = order.Id,
                CustomerId = order.CustomerId,
                ProductIds = [.. order.Items.Select(i => i.ProductId)]
            });

            await db.SaveChangesAsync();
        }

        await next.Execute(context);
    }

    public Task Faulted<TException>(BehaviorExceptionContext<OrderSagaInstance, PaymentSucceededEvent, TException> context,
        IBehavior<OrderSagaInstance, PaymentSucceededEvent> next)
        where TException : Exception => next.Faulted(context);

    public void Probe(ProbeContext context) => context.CreateScope("payment-succeeded-activity");
    public void Accept(StateMachineVisitor visitor) => visitor.Visit(this);
}

public class PaymentFailedCancelActivity(OrderDbContext db) :
    IStateMachineActivity<OrderSagaInstance, PaymentFailedEvent>
{
    public async Task Execute(BehaviorContext<OrderSagaInstance, PaymentFailedEvent> context,
        IBehavior<OrderSagaInstance, PaymentFailedEvent> next)
    {
        var order = await db.Orders.Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.Id == context.Saga.CorrelationId);

        if (order is not null && order.Status == OrderStatus.PaymentPending)
        {
            var oldStatus = order.Status;
            order.CancelDueToPaymentFailure();
            var reason = context.Saga.FailureReason ?? "Payment failed";
            SagaActivityHelper.AddTimelineEvent(db, order, order.Status, $"Order {order.Status}", reason, "Saga");
            await SagaActivityHelper.PublishStatusChanged(context, order, oldStatus, reason);

            await context.Publish(new ReleaseStockCommand
            {
                CorrelationId = order.Id,
                OrderId = order.Id,
                CustomerId = order.CustomerId,
                Reason = reason,
                Items = [.. order.Items.Select(i => new OrderItemInfo
                {
                    ProductId = i.ProductId,
                    Quantity = i.Quantity
                })]
            });

            await db.SaveChangesAsync();
        }

        await next.Execute(context);
    }

    public Task Faulted<TException>(BehaviorExceptionContext<OrderSagaInstance, PaymentFailedEvent, TException> context,
        IBehavior<OrderSagaInstance, PaymentFailedEvent> next)
        where TException : Exception => next.Faulted(context);

    public void Probe(ProbeContext context) => context.CreateScope("payment-failed-cancel-activity");
    public void Accept(StateMachineVisitor visitor) => visitor.Visit(this);
}

public class TimeoutCancelActivity(OrderDbContext db) :
    IStateMachineActivity<OrderSagaInstance, StockTimeoutExpired>
{
    public async Task Execute(BehaviorContext<OrderSagaInstance, StockTimeoutExpired> context,
        IBehavior<OrderSagaInstance, StockTimeoutExpired> next)
    {
        var order = await db.Orders.FirstOrDefaultAsync(o => o.Id == context.Saga.CorrelationId);
        if (order is not null && order.Status == OrderStatus.Pending)
        {
            var oldStatus = order.Status;
            order.CancelDueToStockFailure();
            var reason = context.Saga.FailureReason ?? "Timeout";
            SagaActivityHelper.AddTimelineEvent(db, order, order.Status, $"Order {order.Status}", reason, "Saga:Timeout");
            await SagaActivityHelper.PublishStatusChanged(context, order, oldStatus, reason);
            await db.SaveChangesAsync();
        }

        context.Saga.CompletedAt = DateTime.UtcNow;
        await next.Execute(context);
    }

    public Task Faulted<TException>(BehaviorExceptionContext<OrderSagaInstance, StockTimeoutExpired, TException> context,
        IBehavior<OrderSagaInstance, StockTimeoutExpired> next)
        where TException : Exception => next.Faulted(context);

    public void Probe(ProbeContext context) => context.CreateScope("timeout-cancel-activity");
    public void Accept(StateMachineVisitor visitor) => visitor.Visit(this);
}

public class LateStockReservedActivity :
    IStateMachineActivity<OrderSagaInstance, StockReservedEvent>
{
    public async Task Execute(BehaviorContext<OrderSagaInstance, StockReservedEvent> context,
        IBehavior<OrderSagaInstance, StockReservedEvent> next)
    {
        await context.Publish(new ReleaseStockCommand
        {
            CorrelationId = context.Saga.CorrelationId,
            OrderId = context.Saga.CorrelationId,
            CustomerId = context.Saga.CustomerId,
            Reason = context.Saga.FailureReason ?? "Order was cancelled before stock reservation response arrived.",
            Items = [.. context.Message.Items.Select(i => new OrderItemInfo
            {
                ProductId = i.ProductId,
                Quantity = i.Quantity
            })]
        });

        await next.Execute(context);
    }

    public Task Faulted<TException>(BehaviorExceptionContext<OrderSagaInstance, StockReservedEvent, TException> context,
        IBehavior<OrderSagaInstance, StockReservedEvent> next)
        where TException : Exception => next.Faulted(context);

    public void Probe(ProbeContext context) => context.CreateScope("late-stock-reserved-activity");
    public void Accept(StateMachineVisitor visitor) => visitor.Visit(this);
}

public class PaymentTimeoutCancelActivity(OrderDbContext db) :
    IStateMachineActivity<OrderSagaInstance, PaymentTimeoutExpired>
{
    public async Task Execute(BehaviorContext<OrderSagaInstance, PaymentTimeoutExpired> context,
        IBehavior<OrderSagaInstance, PaymentTimeoutExpired> next)
    {
        var order = await db.Orders.Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.Id == context.Saga.CorrelationId);

        if (order is not null && order.Status == OrderStatus.PaymentPending)
        {
            var oldStatus = order.Status;
            order.CancelDueToPaymentFailure();
            var reason = context.Saga.FailureReason ?? "Payment timeout";
            SagaActivityHelper.AddTimelineEvent(db, order, order.Status, $"Order {order.Status}", reason, "Saga:Timeout");
            await SagaActivityHelper.PublishStatusChanged(context, order, oldStatus, reason);

            await context.Publish(new CancelPaymentCommand
            {
                CorrelationId = order.Id,
                OrderId = order.Id,
                CustomerId = order.CustomerId,
                Amount = order.TotalAmount,
                PaymentMethod = order.PaymentMethod.ToString(),
                Reason = reason
            });

            await db.SaveChangesAsync();
        }

        await next.Execute(context);
    }

    public Task Faulted<TException>(BehaviorExceptionContext<OrderSagaInstance, PaymentTimeoutExpired, TException> context,
        IBehavior<OrderSagaInstance, PaymentTimeoutExpired> next)
        where TException : Exception => next.Faulted(context);

    public void Probe(ProbeContext context) => context.CreateScope("payment-timeout-cancel-activity");
    public void Accept(StateMachineVisitor visitor) => visitor.Visit(this);
}

public class CancelOrderActivity(OrderDbContext db) :
    IStateMachineActivity<OrderSagaInstance, CancelOrderCommand>
{
    public async Task Execute(BehaviorContext<OrderSagaInstance, CancelOrderCommand> context,
        IBehavior<OrderSagaInstance, CancelOrderCommand> next)
    {
        var order = await db.Orders.FirstOrDefaultAsync(o => o.Id == context.Saga.CorrelationId);
        if (order is not null && order.Status == OrderStatus.Pending)
        {
            var oldStatus = order.Status;
            order.CancelDueToStockFailure();
            SagaActivityHelper.AddTimelineEvent(db, order, order.Status, $"Order {order.Status}",
                context.Message.Reason, "Customer");
            await SagaActivityHelper.PublishStatusChanged(context, order, oldStatus, context.Message.Reason);
            await db.SaveChangesAsync();
        }

        context.Saga.CompletedAt = DateTime.UtcNow;
        await next.Execute(context);
    }

    public Task Faulted<TException>(BehaviorExceptionContext<OrderSagaInstance, CancelOrderCommand, TException> context,
        IBehavior<OrderSagaInstance, CancelOrderCommand> next)
        where TException : Exception => next.Faulted(context);

    public void Probe(ProbeContext context) => context.CreateScope("cancel-order-activity");
    public void Accept(StateMachineVisitor visitor) => visitor.Visit(this);
}

public class CancelWithCompensationActivity(OrderDbContext db) :
    IStateMachineActivity<OrderSagaInstance, CancelOrderCommand>
{
    public async Task Execute(BehaviorContext<OrderSagaInstance, CancelOrderCommand> context,
        IBehavior<OrderSagaInstance, CancelOrderCommand> next)
    {
        var order = await db.Orders.Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.Id == context.Saga.CorrelationId);

        if (order is not null && (order.Status == OrderStatus.PaymentPending || order.Status == OrderStatus.Processing))
        {
            var oldStatus = order.Status;
            order.Cancel();
            SagaActivityHelper.AddTimelineEvent(db, order, order.Status, $"Order {order.Status}",
                context.Message.Reason, "Customer");
            await SagaActivityHelper.PublishStatusChanged(context, order, oldStatus, context.Message.Reason);

            if (oldStatus == OrderStatus.PaymentPending && order.IsOnlinePayment())
            {
                await context.Publish(new CancelPaymentCommand
                {
                    CorrelationId = order.Id,
                    OrderId = order.Id,
                    CustomerId = order.CustomerId,
                    Amount = order.TotalAmount,
                    PaymentMethod = order.PaymentMethod.ToString(),
                    Reason = context.Message.Reason
                });
            }

            await db.SaveChangesAsync();
        }

        await next.Execute(context);
    }

    public Task Faulted<TException>(BehaviorExceptionContext<OrderSagaInstance, CancelOrderCommand, TException> context,
        IBehavior<OrderSagaInstance, CancelOrderCommand> next)
        where TException : Exception => next.Faulted(context);

    public void Probe(ProbeContext context) => context.CreateScope("cancel-with-compensation-activity");
    public void Accept(StateMachineVisitor visitor) => visitor.Visit(this);
}

public class LatePaymentSucceededActivity :
    IStateMachineActivity<OrderSagaInstance, PaymentSucceededEvent>
{
    public async Task Execute(BehaviorContext<OrderSagaInstance, PaymentSucceededEvent> context,
        IBehavior<OrderSagaInstance, PaymentSucceededEvent> next)
    {
        var reason = context.Saga.FailureReason ?? "Payment succeeded after order cancellation started.";

        await context.Publish(new CancelPaymentCommand
        {
            CorrelationId = context.Saga.CorrelationId,
            OrderId = context.Saga.CorrelationId,
            CustomerId = context.Saga.CustomerId,
            Amount = context.Message.Amount,
            PaymentMethod = context.Message.PaymentMethod,
            Reason = reason
        });

        await next.Execute(context);
    }

    public Task Faulted<TException>(BehaviorExceptionContext<OrderSagaInstance, PaymentSucceededEvent, TException> context,
        IBehavior<OrderSagaInstance, PaymentSucceededEvent> next)
        where TException : Exception => next.Faulted(context);

    public void Probe(ProbeContext context) => context.CreateScope("late-payment-succeeded-activity");
    public void Accept(StateMachineVisitor visitor) => visitor.Visit(this);
}

public class ReleaseStockAfterPaymentCancelledActivity(OrderDbContext db) :
    IStateMachineActivity<OrderSagaInstance, PaymentFailedEvent>
{
    public async Task Execute(BehaviorContext<OrderSagaInstance, PaymentFailedEvent> context,
        IBehavior<OrderSagaInstance, PaymentFailedEvent> next)
    {
        await ReleaseStockAfterPaymentTerminalState(context, db, context.Message.Reason);
        await next.Execute(context);
    }

    public Task Faulted<TException>(BehaviorExceptionContext<OrderSagaInstance, PaymentFailedEvent, TException> context,
        IBehavior<OrderSagaInstance, PaymentFailedEvent> next)
        where TException : Exception => next.Faulted(context);

    public void Probe(ProbeContext context) => context.CreateScope("release-stock-after-payment-cancelled-activity");
    public void Accept(StateMachineVisitor visitor) => visitor.Visit(this);

    internal static async Task ReleaseStockAfterPaymentTerminalState<TMessage>(
        BehaviorContext<OrderSagaInstance, TMessage> context,
        OrderDbContext db,
        string? reason)
        where TMessage : class
    {
        var order = await db.Orders.Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.Id == context.Saga.CorrelationId);

        if (order is null || order.Status != OrderStatus.Cancelled)
            return;

        await context.Publish(new ReleaseStockCommand
        {
            CorrelationId = order.Id,
            OrderId = order.Id,
            CustomerId = order.CustomerId,
            Reason = string.IsNullOrWhiteSpace(reason) ? context.Saga.FailureReason ?? "Payment was cancelled." : reason,
            Items = [.. order.Items.Select(i => new OrderItemInfo
            {
                ProductId = i.ProductId,
                Quantity = i.Quantity
            })]
        });
    }
}

public class ReleaseStockAfterPaymentRefundedActivity(OrderDbContext db) :
    IStateMachineActivity<OrderSagaInstance, PaymentRefundedEvent>
{
    public async Task Execute(BehaviorContext<OrderSagaInstance, PaymentRefundedEvent> context,
        IBehavior<OrderSagaInstance, PaymentRefundedEvent> next)
    {
        await ReleaseStockAfterPaymentCancelledActivity.ReleaseStockAfterPaymentTerminalState(
            context,
            db,
            context.Message.Reason);

        await next.Execute(context);
    }

    public Task Faulted<TException>(BehaviorExceptionContext<OrderSagaInstance, PaymentRefundedEvent, TException> context,
        IBehavior<OrderSagaInstance, PaymentRefundedEvent> next)
        where TException : Exception => next.Faulted(context);

    public void Probe(ProbeContext context) => context.CreateScope("release-stock-after-payment-refunded-activity");
    public void Accept(StateMachineVisitor visitor) => visitor.Visit(this);
}

internal static class SagaActivityHelper
{
    internal static void AddTimelineEvent(
        OrderDbContext db,
        Domain.Entities.Order order,
        OrderStatus status,
        string title,
        string description,
        string source)
    {
        var timelineEvent = order.AddTimelineEvent(status, title, description, source);
        db.OrderTimelineEvents.Add(timelineEvent);
    }

    internal static Task PublishStatusChanged<TSaga, TMessage>(
        BehaviorContext<TSaga, TMessage> context,
        Domain.Entities.Order order,
        OrderStatus oldStatus,
        string reason)
        where TSaga : class, SagaStateMachineInstance
        where TMessage : class
    {
        if (oldStatus == order.Status) return Task.CompletedTask;

        return context.Publish(new OrderStatusChangedEvent
        {
            CorrelationId = order.Id,
            OrderId = order.Id,
            CustomerId = order.CustomerId,
            OldStatus = oldStatus.ToString(),
            NewStatus = order.Status.ToString(),
            Reason = reason
        });
    }
}