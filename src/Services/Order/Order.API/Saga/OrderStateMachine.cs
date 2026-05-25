using EventBus.Contracts;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Order.Domain.Entities;
using Order.Domain.Enums;
using Order.Infrastructure.Data;
using Order.Infrastructure.Saga;

namespace Order.API.Saga;

/// <summary>
/// MassTransit StateMachine Saga — gom TOÀN BỘ orchestration logic vào 1 nơi.
/// Thay thế consumer thủ công; timeout service chỉ còn bắn timeout event vào state machine.
/// 
/// Flow:
///   Submitted → StockReserving → [COD] → Completed
///                                [Online] → PaymentCreating → PaymentPending → Completed
///                                [Fail/Cancel/Timeout] → Cancelling → Completed
/// </summary>
public class OrderStateMachine : MassTransitStateMachine<OrderSagaInstance>
{
    // ── States ───────────────────────────────────────────────────────────────
    public State Submitted { get; private set; } = null!;
    public State StockReserving { get; private set; } = null!;
    public State PaymentCreating { get; private set; } = null!;
    public State PaymentPending { get; private set; } = null!;
    public State Cancelling { get; private set; } = null!;
    // Final states use SetCompletedWhenFinalized()

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
        // Saga instance state field
        InstanceState(x => x.CurrentState);

        // ── Event Correlation ────────────────────────────────────────────────
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

        // ══════════════════════════════════════════════════════════════════════
        // ── FLOW DEFINITIONS ─────────────────────────────────────────────────
        // ══════════════════════════════════════════════════════════════════════

        // ── 1. Initially: OrderSubmittedEvent ────────────────────────────────
        Initially(
            When(OrderSubmitted)
                .Then(ctx =>
                {
                    ctx.Saga.CustomerId = ctx.Message.CustomerId;
                    ctx.Saga.PaymentMethod = ctx.Message.PaymentMethod;
                    ctx.Saga.IsCOD = !IsOnlinePayment(ctx.Message.PaymentMethod);
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

        // ── 2. StockReserving: chờ stock reservation ─────────────────────────
        During(StockReserving,
            When(OrderSubmitted).Then(_ => { }),

            // ✅ Stock reserved thành công
            When(StockReserved)
                .Then(ctx =>
                {
                    ctx.Saga.StockReservedAt = DateTime.UtcNow;
                    ctx.Saga.UpdatedAt = DateTime.UtcNow;
                })
                .Activity(x => x.OfType<StockReservedActivity>())
                .If(ctx => ctx.Saga.IsCOD,
                    // COD → complete ngay
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
                    // Online payment → chuyển sang payment
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

            // ❌ Stock reservation failed
            When(StockReservationFailed)
                .Then(ctx =>
                {
                    ctx.Saga.FailureReason = ctx.Message.Reason;
                    ctx.Saga.UpdatedAt = DateTime.UtcNow;
                })
                .Activity(x => x.OfType<StockFailedActivity>())
                .Finalize(),

            // ⏰ Stock timeout (15 phút)
            When(StockTimedOut)
                .Then(ctx =>
                {
                    ctx.Saga.FailureReason = "Order timeout while waiting for stock reservation.";
                    ctx.Saga.UpdatedAt = DateTime.UtcNow;
                })
                .Activity(x => x.OfType<TimeoutCancelActivity>())
                .TransitionTo(Cancelling),

            // 🚫 Customer cancel trong khi chờ stock
            When(CancelRequested)
                .Then(ctx =>
                {
                    ctx.Saga.FailureReason = ctx.Message.Reason;
                    ctx.Saga.UpdatedAt = DateTime.UtcNow;
                })
                .Activity(x => x.OfType<CancelOrderActivity>())
                .TransitionTo(Cancelling)
        );

        // ── 3. PaymentCreating: chờ payment service tạo payment ──────────────
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

        // ── 4. PaymentPending: chờ user thanh toán ───────────────────────────
        During(PaymentPending,
            When(OrderSubmitted).Then(_ => { }),
            When(StockReserved).Then(_ => { }),
            When(PaymentCreated).Then(_ => { }),

            // ✅ Payment thành công
            When(PaymentSucceeded)
                .Then(ctx =>
                {
                    ctx.Saga.CompletedAt = DateTime.UtcNow;
                    ctx.Saga.UpdatedAt = DateTime.UtcNow;
                })
                .Activity(x => x.OfType<PaymentSucceededActivity>())
                .Finalize(),

            // ❌ Payment failed
            When(PaymentFailed)
                .Then(ctx =>
                {
                    ctx.Saga.FailureReason = $"Payment failed: {ctx.Message.Reason}";
                    ctx.Saga.UpdatedAt = DateTime.UtcNow;
                })
                .Activity(x => x.OfType<PaymentFailedCancelActivity>())
                .TransitionTo(Cancelling),

            // ⏰ Fallback timeout. Payment service remains the owner of normal payment expiration.
            When(PaymentTimedOut)
                .Then(ctx =>
                {
                    ctx.Saga.FailureReason = "Payment timeout.";
                    ctx.Saga.UpdatedAt = DateTime.UtcNow;
                })
                .Activity(x => x.OfType<PaymentTimeoutCancelActivity>())
                .TransitionTo(Cancelling),

            // 🚫 Customer cancel khi đang chờ payment
            When(CancelRequested)
                .Then(ctx =>
                {
                    ctx.Saga.FailureReason = ctx.Message.Reason;
                    ctx.Saga.UpdatedAt = DateTime.UtcNow;
                })
                .Activity(x => x.OfType<CancelWithCompensationActivity>())
                .TransitionTo(Cancelling)
        );

        // ── 5. Cancelling: đang chờ stock release (compensation) ─────────────
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

            // Ignore duplicate events
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

        // ── Finalized = xóa saga instance khỏi DB ───────────────────────────
        SetCompletedWhenFinalized();
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static bool IsOnlinePayment(string paymentMethod)
        => !string.Equals(paymentMethod, nameof(PaymentMethodType.COD), StringComparison.OrdinalIgnoreCase);
}

// ══════════════════════════════════════════════════════════════════════════════
// ── ACTIVITIES (cập nhật Order entity từ trong saga) ─────────────────────────
// ══════════════════════════════════════════════════════════════════════════════

/// <summary>
/// Khi stock reserved → cập nhật Order entity: MarkStockReserved + publish OrderStatusChanged
/// </summary>
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

/// <summary>
/// Khi stock reservation failed → cancel Order entity
/// </summary>
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

/// <summary>
/// Khi payment thành công → confirm payment trên Order entity + publish RemoveCartItems
/// </summary>
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

            // Remove cart items after successful payment
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

/// <summary>
/// Khi payment failed → cancel Order + publish ReleaseStock
/// </summary>
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

            // Compensation: release stock
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

/// <summary>
/// Stock timeout → cancel Order entity
/// </summary>
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

/// <summary>
/// StockReservedEvent về sau khi order đã bị cancel/timeout thì phải hoàn kho.
/// </summary>
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

/// <summary>
/// Payment timeout → cancel Order + cancel/refund payment, then release stock after payment answers.
/// </summary>
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

/// <summary>
/// Customer cancel khi chưa có stock (đang StockReserving) → chỉ cancel Order
/// </summary>
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

/// <summary>
/// Customer cancel khi đã có stock (PaymentCreating/PaymentPending) → cancel + cancel/refund payment.
/// Stock chỉ được release khi payment service xác nhận failed/refunded.
/// </summary>
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

/// <summary>
/// PaymentSucceededEvent có thể về trễ sau khi order đã vào Cancelling.
/// Khi chưa có refund flow provider thật, CancelPaymentCommand đóng vai trò cancel-or-refund idempotent.
/// </summary>
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

// ── Helper: Publish OrderStatusChanged ───────────────────────────────────────

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
