using EventBus.Extensions;
using EventBus.Infrastructure;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Payment.API.BackgroundJobs;
using Payment.API.Endpoints;
using Payment.API.IntegrationEvents.Consumers;
using Payment.API.PaymentProviders;
using Payment.Infrastructure.Data;
using ServiceDefault;

var builder = WebApplication.CreateBuilder(args);

// ── DbContext ────────────────────────────────────────────────────────────────
builder.AddNpgsqlDbContext<PaymentDbContext>("payment-db", configureDbContextOptions: options =>
{
    options.UseNpgsql(npgsql =>
    {
        npgsql.MigrationsHistoryTable("__EFMigrationsHistory_Payment", "payment");
    });
});

// ── Infrastructure ────────────────────────────────────────────────────────────
builder.AddApiServiceDefaults();
builder.Services.AddHostedService<PaymentTimeoutService>();
builder.Services.AddSingleton<IPaymentProvider, MeiMeiPaymentProvider>();
builder.Services.AddSingleton<IPaymentProvider, MeilyMeilyPaymentProvider>();
builder.Services.AddSingleton<PaymentProviderCatalog>();

if (builder.Environment.IsProduction())
{
    var webhookSecret = builder.Configuration["Payment:WebhookSecret"];
    if (string.IsNullOrWhiteSpace(webhookSecret) ||
        webhookSecret.Contains("development", StringComparison.OrdinalIgnoreCase) ||
        webhookSecret.Contains("change-me", StringComparison.OrdinalIgnoreCase))
    {
        throw new InvalidOperationException("Payment:WebhookSecret must be a production secret.");
    }
}

// ── JWT Auth ──────────────────────────────────────────────────────────────────
builder.Services.AddJwtAuthentication(builder.Configuration);
builder.Services.AddAuthorization();

// ── MassTransit + RabbitMQ ────────────────────────────────────────────────────
builder.Services.AddMassTransit(x =>
{
    x.SetKebabCaseEndpointNameFormatter();

    x.AddEntityFrameworkOutbox<PaymentDbContext>(o =>
    {
        o.UsePostgres();
        o.UseBusOutbox();
    });

    x.AddConsumer<CreatePaymentConsumer>();
    x.AddConsumer<CancelPaymentConsumer>();

    x.UsingRabbitMq((context, cfg) =>
    {
        cfg.UseMessageRetry(r => r.Incremental(3, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2)));
        cfg.Host(builder.Configuration.GetRequiredConnectionStringUri("rabbitmq"));

        cfg.ReceiveEndpoint("create-payment", e =>
        {
            e.UseEntityFrameworkOutbox<PaymentDbContext>(context);
            e.ConfigureConsumer<CreatePaymentConsumer>(context);
        });

        cfg.ReceiveEndpoint("cancel-payment", e =>
        {
            e.UseEntityFrameworkOutbox<PaymentDbContext>(context);
            e.ConfigureConsumer<CancelPaymentConsumer>(context);
        });
    });
});

// ── Redis Idempotency ────────────────────────────────────────────────────────
builder.AddRedisClient("redis");
builder.Services.AddRedisIdempotency();

var app = builder.Build();

await app.MigrateDatabaseAsync<PaymentDbContext>();

// ── Middleware ────────────────────────────────────────────────────────────────
app.UseApiServiceDefaults();

app.UseAuthentication();
app.UseAuthorization();

// ── Endpoints ─────────────────────────────────────────────────────────────────
app.MapApiHealthChecks();
app.MapPaymentEndpoints();

app.Run();

public partial class Program;
