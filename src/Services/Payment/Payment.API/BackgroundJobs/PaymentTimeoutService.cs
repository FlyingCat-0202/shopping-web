using EventBus.Contracts;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Payment.Domain.Enums;
using Payment.Infrastructure.Data;

namespace Payment.API.BackgroundJobs;

public class PaymentTimeoutService(IServiceProvider serviceProvider, ILogger<PaymentTimeoutService> logger)
    : BackgroundService
{
    private const string PaymentFailedRoutingKey = "payment-failed";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = serviceProvider.CreateScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<PaymentDbContext>();
                var publishEndpoint = scope.ServiceProvider.GetRequiredService<IPublishEndpoint>();

                var timeoutThreshold = DateTime.UtcNow.AddMinutes(-10);
                var expiredPayments = await dbContext.Payments
                    .Where(p => p.Status == PaymentStatus.Pending && p.CreatedAt < timeoutThreshold)
                    .OrderBy(p => p.CreatedAt)
                    .Take(100)
                    .ToListAsync(stoppingToken);

                foreach (var payment in expiredPayments)
                {
                    payment.MarkFailed("Thanh toán quá thời gian xử lý.");

                    await publishEndpoint.Publish(new PaymentFailedEvent
                    {
                        PaymentId = payment.Id,
                        OrderId = payment.OrderId,
                        CustomerId = payment.CustomerId,
                        Reason = payment.FailureReason ?? "Thanh toán quá thời gian xử lý."
                    }, ctx => ctx.SetRoutingKey(PaymentFailedRoutingKey), stoppingToken);

                    logger.LogWarning(
                        "Payment {PaymentId} của Order {OrderId} đã bị fail do quá thời gian xử lý.",
                        payment.Id,
                        payment.OrderId);
                }

                if (expiredPayments.Count > 0)
                    await dbContext.SaveChangesAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Lỗi khi chạy PaymentTimeoutService");
            }

            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
        }
    }
}
