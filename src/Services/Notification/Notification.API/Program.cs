using EventBus.Extensions;
using EventBus.Infrastructure;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi;
using Notification.API.Endpoints;
using Notification.API.IntegrationEvents.Consumers;
using Notification.Infrastructure.Data;
using ServiceDefault;

var builder = WebApplication.CreateBuilder(args);

// ── DbContext ────────────────────────────────────────────────────────────────
builder.Services.AddDbContext<NotificationDbContext>(options =>
{
    options.UseNpgsql(
        builder.Configuration.GetConnectionString("notification-db")
            ?? builder.Configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException(
                "Missing connection string. Set ConnectionStrings__notification-db or run the service through Aspire AppHost."),
        npgsql =>
        {
            npgsql.MigrationsHistoryTable("__EFMigrationsHistory_Notification", "notification");
            npgsql.EnableRetryOnFailure(
                maxRetryCount: 5,
                maxRetryDelay: TimeSpan.FromSeconds(30),
                errorCodesToAdd: null);
        });
});

// ── Infrastructure ───────────────────────────────────────────────────────────
builder.AddApiServiceDefaults();

// ── Swagger / OpenAPI ─────────────────────────────────────────────────────────
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "Notification API", Version = "v1" });
    var bearerSchemeId = "Bearer";
    c.AddSecurityDefinition(bearerSchemeId, new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT"
    });
    c.AddSecurityRequirement(doc => new OpenApiSecurityRequirement
    {
        [new OpenApiSecuritySchemeReference(bearerSchemeId, doc)] = []
    });
});

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
        cfg.Host(new Uri(builder.Configuration.GetConnectionString("rabbitmq") ?? "amqp://guest:guest@localhost:5672/"));

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
var redisConnectionString = builder.Configuration.GetConnectionString("redis") ??
                            builder.Configuration.GetConnectionString("DefaultConnection") ??
                            throw new InvalidOperationException("Missing connection string for redis");
builder.Services.AddRedisIdempotency(redisConnectionString);

var app = builder.Build();

await app.MigrateDatabaseAsync<NotificationDbContext>();

// ── Middleware ───────────────────────────────────────────────────────────────
app.UseApiServiceDefaults();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "Notification API v1"));
}

app.UseAuthentication();
app.UseAuthorization();

// ── Endpoints ────────────────────────────────────────────────────────────────
app.MapHealthChecks("/health");
app.MapNotificationEndpoints();

app.Run();
