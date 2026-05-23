using EventBus.Contracts;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Order.Domain.Entities;
using Order.Domain.Enums;
using Order.Infrastructure.Data;

namespace Order.API.IntegrationEvents.Consumer;

public class OrderSagaConsumer(OrderDbContext dbContext, ILogger<OrderSagaConsumer> logger) :
    IConsumer<OrderSubmittedEvent>,
    IConsumer<StockReservedEvent>,
    IConsumer<StockReservationFailedEvent>,
    IConsumer<PaymentCreatedEvent>,
    IConsumer<PaymentSucceededEvent>,
    IConsumer<PaymentFailedEvent>,
    IConsumer<StockReleasedEvent>
{
    private const string ReserveStockRoutingKey = "reserve-stock";
    private const string ReleaseStockRoutingKey = "release-stock";
    private const string CreatePaymentRoutingKey = "create-payment";
    private const string RemoveCartItemsRoutingKey = "remove-cart-items";
    private const string OrderStatusChangedRoutingKey = "order-status-changed";

    public async Task Consume(ConsumeContext<OrderSubmittedEvent> context)
    {
        var message = context.Message;

        try
        {
            var saga = await dbContext.OrderSagaStates
                .FirstOrDefaultAsync(s => s.OrderId == message.OrderId, context.CancellationToken);

            if (saga is null)
            {
                saga = OrderSagaState.Start(message.OrderId, message.CustomerId, message.PaymentMethod);
                dbContext.OrderSagaStates.Add(saga);
            }

            if (saga.IsCompleted)
                return;

            saga.MoveTo(OrderSagaSteps.StockReservationPending);

            await context.Publish(new ReserveStockCommand
            {
                CorrelationId = GetCorrelationId(message.CorrelationId, message.OrderId),
                OrderId = message.OrderId,
                CustomerId = message.CustomerId,
                Items = message.Items
            }, ctx => ctx.SetRoutingKey(ReserveStockRoutingKey), context.CancellationToken);

            await dbContext.SaveChangesAsync(context.CancellationToken);

            logger.LogInformation("Order saga {OrderId} đã gửi ReserveStockCommand.", message.OrderId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Lỗi khi bắt đầu order saga cho Order {OrderId}", message.OrderId);
            throw;
        }
    }

    public async Task Consume(ConsumeContext<StockReservedEvent> context)
    {
        var message = context.Message;

        try
        {
            var order = await LoadOrderAsync(message.OrderId, context.CancellationToken);
            if (order is null)
                return;

            var saga = await LoadOrCreateSagaAsync(order, context.CancellationToken);
            if (saga.IsCompleted && order.Status != OrderStatus.Cancelled)
                return;

            if (order.Status == OrderStatus.Cancelled)
            {
                await PublishReleaseStockCommand(context, order, "Order was cancelled before stock reservation response arrived.");
                if (!saga.IsCompleted)
                    saga.MoveTo(OrderSagaSteps.StockReleasePending);
                await dbContext.SaveChangesAsync(context.CancellationToken);
                return;
            }

            if (order.Status == OrderStatus.Pending)
            {
                var oldStatus = order.Status;
                var prices = message.Items.ToDictionary(i => i.ProductId, i => i.UnitPrice);
                order.MarkStockReserved(prices);

                saga.StockReservedAt = DateTime.UtcNow;
                saga.TotalAmount = order.TotalAmount;

                if (order.IsOnlinePayment())
                {
                    saga.MoveTo(OrderSagaSteps.PaymentCreationPending);
                    await PublishCreatePaymentCommand(context, order);
                    await PublishOrderStatusChanged(context, order, oldStatus, "Stock reserved, waiting for online payment.");
                }
                else
                {
                    await PublishRemoveCartItemsCommand(context, order);
                    await PublishOrderStatusChanged(context, order, oldStatus, "Stock reserved for COD order.");
                    saga.Complete();
                }

                await dbContext.SaveChangesAsync(context.CancellationToken);
                return;
            }

            if (order.Status == OrderStatus.PaymentPending && order.IsOnlinePayment())
            {
                if (saga.CurrentStep == OrderSagaSteps.PaymentPending)
                    return;

                saga.MoveTo(OrderSagaSteps.PaymentCreationPending);
                await PublishCreatePaymentCommand(context, order);
                await dbContext.SaveChangesAsync(context.CancellationToken);
                return;
            }

            if (order.Status == OrderStatus.Processing && !order.IsOnlinePayment())
            {
                await PublishRemoveCartItemsCommand(context, order);
                saga.Complete();
                await dbContext.SaveChangesAsync(context.CancellationToken);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Lỗi khi xử lý StockReservedEvent cho Order {OrderId}", message.OrderId);
            throw;
        }
    }

    public async Task Consume(ConsumeContext<StockReservationFailedEvent> context)
    {
        var message = context.Message;

        try
        {
            var order = await LoadOrderAsync(message.OrderId, context.CancellationToken);
            if (order is null)
                return;

            var saga = await LoadOrCreateSagaAsync(order, context.CancellationToken);
            if (saga.IsCompleted)
                return;

            if (order.Status == OrderStatus.Pending)
            {
                var oldStatus = order.Status;
                order.CancelDueToStockFailure();
                await PublishOrderStatusChanged(
                    context,
                    order,
                    oldStatus,
                    $"Stock reservation failed: {message.Reason}");
            }

            saga.Fail(message.Reason);
            await dbContext.SaveChangesAsync(context.CancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Lỗi khi xử lý StockReservationFailedEvent cho Order {OrderId}", message.OrderId);
            throw;
        }
    }

    public async Task Consume(ConsumeContext<PaymentCreatedEvent> context)
    {
        var message = context.Message;

        try
        {
            var order = await LoadOrderAsync(message.OrderId, context.CancellationToken);
            if (order is null)
                return;

            var saga = await LoadOrCreateSagaAsync(order, context.CancellationToken);
            if (saga.IsCompleted)
                return;

            if (order.Status != OrderStatus.PaymentPending)
                return;

            saga.PaymentCreatedAt = DateTime.UtcNow;
            saga.MoveTo(OrderSagaSteps.PaymentPending);

            await dbContext.SaveChangesAsync(context.CancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Lỗi khi xử lý PaymentCreatedEvent cho Order {OrderId}", message.OrderId);
            throw;
        }
    }

    public async Task Consume(ConsumeContext<PaymentSucceededEvent> context)
    {
        var message = context.Message;

        try
        {
            var order = await LoadOrderAsync(message.OrderId, context.CancellationToken);
            if (order is null)
                return;

            var saga = await LoadOrCreateSagaAsync(order, context.CancellationToken);
            if (saga.IsCompleted)
                return;

            if (order.Status == OrderStatus.PaymentPending)
            {
                var oldStatus = order.Status;
                order.ConfirmPayment();
                await PublishRemoveCartItemsCommand(context, order);
                await PublishOrderStatusChanged(context, order, oldStatus, "Payment succeeded.");
            }

            if (order.Status == OrderStatus.Processing)
                saga.Complete();

            await dbContext.SaveChangesAsync(context.CancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Lỗi khi xử lý PaymentSucceededEvent cho Order {OrderId}", message.OrderId);
            throw;
        }
    }

    public async Task Consume(ConsumeContext<PaymentFailedEvent> context)
    {
        var message = context.Message;

        try
        {
            var order = await LoadOrderAsync(message.OrderId, context.CancellationToken);
            if (order is null)
                return;

            var saga = await LoadOrCreateSagaAsync(order, context.CancellationToken);
            if (saga.IsCompleted)
                return;

            if (order.Status == OrderStatus.PaymentPending)
            {
                var oldStatus = order.Status;
                order.CancelDueToPaymentFailure();
                await PublishReleaseStockCommand(context, order, $"Payment failed: {message.Reason}");
                await PublishOrderStatusChanged(context, order, oldStatus, $"Payment failed: {message.Reason}");
                saga.FailureReason = message.Reason;
                saga.MoveTo(OrderSagaSteps.StockReleasePending);
            }

            await dbContext.SaveChangesAsync(context.CancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Lỗi khi xử lý PaymentFailedEvent cho Order {OrderId}", message.OrderId);
            throw;
        }
    }

    public async Task Consume(ConsumeContext<StockReleasedEvent> context)
    {
        var message = context.Message;

        try
        {
            var saga = await dbContext.OrderSagaStates
                .FirstOrDefaultAsync(s => s.OrderId == message.OrderId, context.CancellationToken);

            if (saga is null || saga.IsCompleted)
                return;

            saga.Fail(message.Reason);
            await dbContext.SaveChangesAsync(context.CancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Lỗi khi xử lý StockReleasedEvent cho Order {OrderId}", message.OrderId);
            throw;
        }
    }

    private Task<Domain.Entities.Order?> LoadOrderAsync(Guid orderId, CancellationToken cancellationToken)
        => dbContext.Orders
            .Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.Id == orderId, cancellationToken);

    private async Task<OrderSagaState> LoadOrCreateSagaAsync(
        Domain.Entities.Order order,
        CancellationToken cancellationToken)
    {
        var saga = await dbContext.OrderSagaStates
            .FirstOrDefaultAsync(s => s.OrderId == order.Id, cancellationToken);

        if (saga is not null)
            return saga;

        saga = OrderSagaState.Start(order.Id, order.CustomerId, order.PaymentMethod.ToString());
        dbContext.OrderSagaStates.Add(saga);
        return saga;
    }

    private static Task PublishCreatePaymentCommand(ConsumeContext context, Domain.Entities.Order order)
        => context.Publish(new CreatePaymentCommand
        {
            CorrelationId = order.Id,
            OrderId = order.Id,
            CustomerId = order.CustomerId,
            Amount = order.TotalAmount,
            PaymentMethod = order.PaymentMethod.ToString()
        }, ctx => ctx.SetRoutingKey(CreatePaymentRoutingKey), context.CancellationToken);

    private static Task PublishReleaseStockCommand(
        ConsumeContext context,
        Domain.Entities.Order order,
        string reason)
        => context.Publish(new ReleaseStockCommand
        {
            CorrelationId = order.Id,
            OrderId = order.Id,
            CustomerId = order.CustomerId,
            Reason = reason,
            Items = [.. order.Items.Select(od => new OrderItemInfo
            {
                ProductId = od.ProductId,
                Quantity = od.Quantity
            })]
        }, ctx => ctx.SetRoutingKey(ReleaseStockRoutingKey), context.CancellationToken);

    private static Task PublishRemoveCartItemsCommand(ConsumeContext context, Domain.Entities.Order order)
        => context.Publish(new RemoveCartItemsCommand
        {
            CorrelationId = order.Id,
            OrderId = order.Id,
            CustomerId = order.CustomerId,
            ProductIds = [.. order.Items.Select(od => od.ProductId)]
        }, ctx => ctx.SetRoutingKey(RemoveCartItemsRoutingKey), context.CancellationToken);

    private static Task PublishOrderStatusChanged(
        ConsumeContext context,
        Domain.Entities.Order order,
        OrderStatus oldStatus,
        string reason)
        => context.Publish(new OrderStatusChangedEvent
        {
            CorrelationId = order.Id,
            OrderId = order.Id,
            CustomerId = order.CustomerId,
            OldStatus = oldStatus.ToString(),
            NewStatus = order.Status.ToString(),
            Reason = reason
        }, ctx => ctx.SetRoutingKey(OrderStatusChangedRoutingKey), context.CancellationToken);

    private static Guid GetCorrelationId(Guid correlationId, Guid orderId)
        => correlationId == Guid.Empty ? orderId : correlationId;
}
