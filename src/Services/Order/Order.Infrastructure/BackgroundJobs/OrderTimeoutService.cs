using EventBus.Contracts;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Order.Domain.Entities;
using Order.Domain.Enums;
using Order.Infrastructure.Data;

namespace Order.Infrastructure.BackgroundJobs;

public class OrderTimeoutService(IServiceProvider serviceProvider, ILogger<OrderTimeoutService> logger)
    : BackgroundService
{
    private const string OrderStatusChangedRoutingKey = "order-status-changed";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = serviceProvider.CreateScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<OrderDbContext>();
                var publishEndpoint = scope.ServiceProvider.GetRequiredService<IPublishEndpoint>();

                var timeoutThreshold = DateTime.UtcNow.AddMinutes(-15);
                var expiredOrders = await dbContext.Orders
                    .Where(o => o.Status == OrderStatus.Pending && o.OrderDate < timeoutThreshold)
                    .OrderBy(o => o.OrderDate)
                    .Take(100)
                    .ToListAsync(stoppingToken);

                if (expiredOrders.Count > 0)
                {
                    foreach (var order in expiredOrders)
                    {
                        var oldStatus = order.Status;
                        order.CancelDueToStockFailure();
                        var reason = "Order timeout while waiting for stock reservation.";

                        var saga = await dbContext.OrderSagaStates
                            .FirstOrDefaultAsync(s => s.OrderId == order.Id, stoppingToken);

                        if (saga is null)
                        {
                            saga = OrderSagaState.Start(order.Id, order.CustomerId, order.PaymentMethod.ToString());
                            dbContext.OrderSagaStates.Add(saga);
                        }

                        if (!saga.IsCompleted)
                            saga.Fail(reason);

                        await publishEndpoint.Publish(new OrderStatusChangedEvent
                        {
                            CorrelationId = order.Id,
                            OrderId = order.Id,
                            CustomerId = order.CustomerId,
                            OldStatus = oldStatus.ToString(),
                            NewStatus = order.Status.ToString(),
                            Reason = reason
                        }, ctx => ctx.SetRoutingKey(OrderStatusChangedRoutingKey), stoppingToken);

                        logger.LogWarning("Order {OrderId} đã bị hủy tự động do quá thời gian chờ giữ kho", order.Id);
                    }

                    await dbContext.SaveChangesAsync(stoppingToken);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Lỗi khi chạy OrderTimeoutService");
            }

            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
        }
    }
}
