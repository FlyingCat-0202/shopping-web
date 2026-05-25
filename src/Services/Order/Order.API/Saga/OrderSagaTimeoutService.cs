using EventBus.Contracts;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Order.Infrastructure.Data;

namespace Order.API.Saga;

public sealed class OrderSagaTimeoutService(
    IServiceProvider serviceProvider,
    ILogger<OrderSagaTimeoutService> logger) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan StockReservationTimeout = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan PaymentCreationTimeout = TimeSpan.FromMinutes(10);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(PollInterval);

        while (!stoppingToken.IsCancellationRequested)
        {
            await PublishExpiredSagaTimeouts(stoppingToken);

            try
            {
                await timer.WaitForNextTickAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    private async Task PublishExpiredSagaTimeouts(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<OrderDbContext>();
            var publishEndpoint = scope.ServiceProvider.GetRequiredService<IPublishEndpoint>();

            var now = DateTime.UtcNow;
            var stockThreshold = now.Subtract(StockReservationTimeout);
            var paymentCreationThreshold = now.Subtract(PaymentCreationTimeout);

            var stockTimeouts = await db.OrderSagaInstances
                .AsNoTracking()
                .Where(s => s.CurrentState == nameof(OrderStateMachine.StockReserving)
                    && s.UpdatedAt < stockThreshold)
                .OrderBy(s => s.UpdatedAt)
                .Take(100)
                .Select(s => s.CorrelationId)
                .ToListAsync(cancellationToken);

            foreach (var orderId in stockTimeouts)
            {
                await publishEndpoint.Publish(new StockTimeoutExpired
                {
                    OrderId = orderId,
                    ExpiredAt = now
                }, cancellationToken);
            }

            var paymentCreationTimeouts = await db.OrderSagaInstances
                .AsNoTracking()
                .Where(s => s.CurrentState == nameof(OrderStateMachine.PaymentCreating)
                    && s.UpdatedAt < paymentCreationThreshold)
                .OrderBy(s => s.UpdatedAt)
                .Take(100)
                .Select(s => s.CorrelationId)
                .ToListAsync(cancellationToken);

            foreach (var orderId in paymentCreationTimeouts)
            {
                await publishEndpoint.Publish(new PaymentTimeoutExpired
                {
                    OrderId = orderId,
                    ExpiredAt = now
                }, cancellationToken);
            }

            if (stockTimeouts.Count > 0 || paymentCreationTimeouts.Count > 0)
            {
                logger.LogWarning(
                    "Published {StockTimeoutCount} stock timeout(s) and {PaymentCreationTimeoutCount} payment creation timeout(s).",
                    stockTimeouts.Count,
                    paymentCreationTimeouts.Count);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed while checking order saga timeouts.");
        }
    }
}
