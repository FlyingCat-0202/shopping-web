using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Order.Domain.Enums;
using Order.Infrastructure.Data;

namespace Order.Infrastructure.BackgroundJobs;

public class OrderTimeoutService(IServiceProvider serviceProvider, ILogger<OrderTimeoutService> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = serviceProvider.CreateScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<OrderDbContext>();

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
                        order.CancelDueToStockFailure();
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
