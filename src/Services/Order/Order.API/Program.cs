using EventBus.Extensions;
using EventBus.Infrastructure;
using FluentValidation;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Order.API.Endpoints;
using Order.API.Saga;
using Order.API.Validators;
using Order.Infrastructure.Data;
using Order.Infrastructure.Saga;
using ServiceDefault;


var builder = WebApplication.CreateBuilder(args);

// ── DbContext ────────────────────────────────────────────────────────────────
builder.AddNpgsqlDbContext<OrderDbContext>("order-db", configureDbContextOptions: options =>
{
    options.UseNpgsql(npgsql =>
    {
        npgsql.MigrationsHistoryTable("__EFMigrationsHistory_Order", "order");
    });
});

// ── Infrastructure ────────────────────────────────────────────────────────────
builder.AddApiServiceDefaults();
builder.Services.AddHostedService<OrderSagaTimeoutService>();
builder.Services.AddValidatorsFromAssemblyContaining<CreateOrderValidator>();

// ── JWT Auth ──────────────────────────────────────────────────────────────────
builder.Services.AddJwtAuthentication(builder.Configuration);
builder.Services.AddAuthorization();

// ── MassTransit + RabbitMQ + Saga StateMachine ───────────────────────────────
builder.Services.AddMassTransit(x =>
{
    x.SetKebabCaseEndpointNameFormatter();

    x.AddEntityFrameworkOutbox<OrderDbContext>(o =>
    {
        o.UsePostgres();
        o.UseBusOutbox();
    });

    // ── StateMachine Saga (thay thế OrderSagaConsumer + OrderTimeoutService) ─
    x.AddSagaStateMachine<OrderStateMachine, OrderSagaInstance>()
        .EntityFrameworkRepository(r =>
        {
            r.ExistingDbContext<OrderDbContext>();
            r.UsePostgres();
        });

    x.UsingRabbitMq((context, cfg) =>
    {
        cfg.UseMessageRetry(r => r.Incremental(3, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2)));
        cfg.Host(new Uri(builder.Configuration.GetConnectionString("rabbitmq") ?? "amqp://guest:guest@localhost:5672/"));

        cfg.ReceiveEndpoint("order-saga", e =>
        {
            e.UseEntityFrameworkOutbox<OrderDbContext>(context);
            e.ConfigureSaga<OrderSagaInstance>(context);
        });
    });
});

// ── Add Redis ────────────────────────────────────────────────────────────────
builder.AddRedisClient("redis");
builder.Services.AddRedisIdempotency();

var app = builder.Build();

await app.MigrateDatabaseAsync<OrderDbContext>();

// ── Middleware ────────────────────────────────────────────────────────────────
app.UseApiServiceDefaults();

app.UseAuthentication();
app.UseAuthorization();

// ── Endpoints ─────────────────────────────────────────────────────────────────
app.MapHealthChecks("/health");
app.MapOrderEndpoints();

app.Run();
