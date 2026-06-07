using EventBus.Extensions;
using EventBus.Infrastructure;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Notification.API.Endpoints;
using Notification.API.IntegrationEvents.Consumers;
using Notification.Infrastructure.Data;
using ServiceDefault;

var builder = WebApplication.CreateBuilder(args);

// ── DbContext ────────────────────────────────────────────────────────────────
builder.AddNpgsqlDbContext<NotificationDbContext>("notification-db", configureDbContextOptions: options =>
{
    options.UseNpgsql(npgsql =>
    {
        npgsql.MigrationsHistoryTable("__EFMigrationsHistory_Notification", "notification");
    });
});

// ── Infrastructure ───────────────────────────────────────────────────────────
builder.AddApiServiceDefaults();

// ── JWT Auth ─────────────────────────────────────────────────────────────────
builder.Services.AddJwtAuthentication(builder.Configuration);
builder.Services.AddAuthorization();

// ── MassTransit + RabbitMQ ───────────────────────────────────────────────────
builder.Services.AddMassTransit(x =>
{
    x.SetKebabCaseEndpointNameFormatter();

    x.AddEntityFrameworkOutbox<NotificationDbContext>(o =>
    {
        o.UsePostgres();
        o.UseBusOutbox();
    });

    x.AddConsumer<CustomerProfileChangedConsumer>();
    x.AddConsumer<OrderStatusChangedConsumer>();

    x.UsingRabbitMq((context, cfg) =>
    {
        cfg.UseMessageRetry(r => r.Incremental(3, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2)));
        cfg.Host(builder.Configuration.GetRequiredConnectionStringUri("rabbitmq"));

        cfg.ReceiveEndpoint("customer-profile-changed", e =>
        {
            e.UseEntityFrameworkOutbox<NotificationDbContext>(context);
            e.ConfigureConsumer<CustomerProfileChangedConsumer>(context);
        });

        cfg.ReceiveEndpoint("order-status-changed", e =>
        {
            e.UseEntityFrameworkOutbox<NotificationDbContext>(context);
            e.ConfigureConsumer<OrderStatusChangedConsumer>(context);
        });
    });
});

// ── Redis Idempotency ────────────────────────────────────────────────────────
builder.AddRedisClient("redis");
builder.Services.AddRedisIdempotency();

var app = builder.Build();

await app.MigrateDatabaseAsync<NotificationDbContext>();

// ── Middleware ───────────────────────────────────────────────────────────────
app.UseApiServiceDefaults();

app.UseAuthentication();
app.UseAuthorization();

// ── Endpoints ────────────────────────────────────────────────────────────────
app.MapApiHealthChecks();
app.MapNotificationEndpoints();

app.Run();
