using EventBus.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Notification.API.IntegrationEvents.Consumers;
using Notification.Infrastructure.Data;
using Shouldly;
using Testcontainers.PostgreSql;
using Xunit;

namespace Service.IntegrationTests;

public class NotificationConsumerTests
{
    [SkippableFact]
    public async Task OrderStatusChangedConsumerStoresNotificationOnce()
    {
        Skip.IfNot(ServiceIntegrationTestEnvironment.IsDockerAvailable(), "Docker is required for PostgreSQL integration tests.");

        await using var postgres = new PostgreSqlBuilder()
            .WithImage("postgres:17-alpine")
            .WithDatabase("notification-db")
            .WithUsername("postgres")
            .WithPassword("postgres")
            .Build();

        await postgres.StartAsync();

        var options = new DbContextOptionsBuilder<NotificationDbContext>()
            .UseNpgsql(postgres.GetConnectionString(), npgsql =>
            {
                npgsql.MigrationsHistoryTable("__EFMigrationsHistory_Notification", "notification");
            })
            .Options;

        await using var db = new NotificationDbContext(options);
        await db.Database.MigrateAsync();

        var consumer = new OrderStatusChangedConsumer(
            db,
            NullLogger<OrderStatusChangedConsumer>.Instance);

        var message = new OrderStatusChangedEvent
        {
            EventId = Guid.NewGuid(),
            CorrelationId = Guid.NewGuid(),
            OrderId = Guid.NewGuid(),
            CustomerId = Guid.NewGuid(),
            OldStatus = "Processing",
            NewStatus = "Shipped",
            Reason = "Carrier accepted package.",
            OccurredAt = DateTime.UtcNow
        };

        await consumer.HandleAsync(message);
        await consumer.HandleAsync(message);

        var notifications = await db.Notifications.AsNoTracking().ToListAsync();
        notifications.Count.ShouldBe(1);
        notifications[0].CustomerId.ShouldBe(message.CustomerId);
        notifications[0].Type.ShouldBe("OrderShipped");
        notifications[0].SourceEventId.ShouldBe(message.EventId);
        notifications[0].DataJson.ShouldNotBeNull();
        notifications[0].DataJson!.ShouldContain(message.OrderId.ToString());
    }
}
