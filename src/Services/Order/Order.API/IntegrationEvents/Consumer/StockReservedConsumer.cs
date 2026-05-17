using EventBus.Contracts;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Order.Domain.Enums;
using Order.Infrastructure.Data;

namespace Order.API.IntegrationEvents.Consumer;

public class StockReservedConsumer(OrderDbContext dbContext, ILogger<StockReservedConsumer> logger)
    : IConsumer<StockReservedEvent>
{
    private const string PaymentRequestedRoutingKey = "payment-requested";
    private const string OrderCancelledRoutingKey = "order-cancelled";
    private const string CartItemsRemovedRoutingKey = "cart-items-removed";

    public async Task Consume(ConsumeContext<StockReservedEvent> context)
    {
        try
        {
            var message = context.Message;
            var order = await dbContext.Orders
                .Include(o => o.Items)
                .FirstOrDefaultAsync(o => o.Id == message.OrderId, context.CancellationToken);

            if (order is null) return; // idempotent

            if (order.Status == OrderStatus.Pending)
            {
                // ── Cập nhật trạng thái đơn hàng và giá tiền ───────────────────────────────────────────────
                var prices = message.Items.ToDictionary(i => i.ProductId, i => i.UnitPrice);
                order.MarkStockReserved(prices);

                if (order.IsOnlinePayment())
                {
                    await PublishPaymentRequested(context, order);
                    logger.LogInformation(
                        "Order {OrderId} đã giữ kho và gửi yêu cầu thanh toán online với TotalAmount = {TotalAmount}",
                        order.Id,
                        order.TotalAmount);
                }
                else
                {
                    await PublishCartItemsRemoved(context, order);
                    logger.LogInformation(
                        "Order COD {OrderId} đã giữ kho và chuyển sang Processing với TotalAmount = {TotalAmount}",
                        order.Id,
                        order.TotalAmount);
                }

                await dbContext.SaveChangesAsync(context.CancellationToken);
            }
            else if (order.Status == OrderStatus.PaymentPending)
            {
                await PublishPaymentRequested(context, order);

                await dbContext.SaveChangesAsync(context.CancellationToken);
            }
            else if (order.Status == OrderStatus.Cancelled)
            {
                var items = order.Items
                    .Select(od => new OrderItemInfo { ProductId = od.ProductId, Quantity = od.Quantity })
                    .ToList();

                await context.Publish(new OrderCancelledEvent { OrderId = order.Id, Items = items },
                    ctx => ctx.SetRoutingKey(OrderCancelledRoutingKey),
                    context.CancellationToken);

                await dbContext.SaveChangesAsync(context.CancellationToken);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Lỗi khi xử lý StockReservedEvent cho Order {OrderId}", context.Message.OrderId);
            throw;
        }
    }

    private static Task PublishPaymentRequested(ConsumeContext context, Domain.Entities.Order order)
        => context.Publish(new PaymentRequestedEvent
        {
            OrderId = order.Id,
            CustomerId = order.CustomerId,
            Amount = order.TotalAmount,
            PaymentMethod = order.PaymentMethod.ToString()
        }, ctx => ctx.SetRoutingKey(PaymentRequestedRoutingKey), context.CancellationToken);

    private static Task PublishCartItemsRemoved(ConsumeContext context, Domain.Entities.Order order)
    {
        var productIds = order.Items.Select(od => od.ProductId).ToList();

        return context.Publish(new CartItemsRemovedEvent
        {
            CustomerId = order.CustomerId,
            ProductIds = productIds
        }, ctx => ctx.SetRoutingKey(CartItemsRemovedRoutingKey), context.CancellationToken);
    }
}
