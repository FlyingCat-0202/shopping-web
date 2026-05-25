using EventBus.Extensions;
using EventBus.Infrastructure;
using FluentValidation;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi;
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

// ── Swagger / OpenAPI ─────────────────────────────────────────────────────────
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "Order API", Version = "v1" });
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

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "Order API v1"));
}

app.UseAuthentication();
app.UseAuthorization();

// ── Endpoints ─────────────────────────────────────────────────────────────────
app.MapHealthChecks("/health");
app.MapOrderEndpoints();

app.Run();
