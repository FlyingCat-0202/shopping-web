using Elastic.Clients.Elasticsearch;
using EventBus.Extensions;
using EventBus.Infrastructure;
using FluentValidation;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Product.API.Endpoints;
using Product.API.IntegrationEvents.Consumers.Elastic;
using Product.API.IntegrationEvents.Consumers.OrderSupportConsumer;
using Product.API.IntegrationEvents.Consumers.Self;
using Product.API.Validators;
using Product.Domain.Entities;
using Product.Infrastructure.AISearch;
using Product.Infrastructure.Data;
using ServiceDefault;

var builder = WebApplication.CreateBuilder(args);

// ── DbContext ────────────────────────────────────────────────────────────────
builder.AddNpgsqlDbContext<ProductDbContext>("product-db", configureDbContextOptions: options =>
{
    options.UseNpgsql(npgsql =>
    {
        npgsql.MigrationsHistoryTable("__EFMigrationsHistory_Product", "product");
    });
});

// ── Infrastructure ────────────────────────────────────────────────────────────
builder.AddApiServiceDefaults();
builder.Services.AddValidatorsFromAssemblyContaining<ProductRequestValidator>();
builder.Services.AddScoped<IStockReservationService, StockReservationService>();
builder.AddElasticsearchClient(
    "elasticsearch",
    configureClientSettings: settings => settings.DefaultIndex("products"));

// ── JWT Auth ──────────────────────────────────────────────────────────────────
builder.Services.AddJwtAuthentication(builder.Configuration);
builder.Services.AddAuthorization();

// ── MassTransit + RabbitMQ ────────────────────────────────────────────────────
builder.Services.AddMassTransit(x =>
{
    x.SetKebabCaseEndpointNameFormatter();

    x.AddEntityFrameworkOutbox<ProductDbContext>(o =>
    {
        o.UsePostgres();
        o.UseBusOutbox();
    });

    x.AddConsumer<ReserveStockConsumer>();
    x.AddConsumer<ReleaseStockConsumer>();
    x.AddConsumer<ProductCreationConsumer>();
    x.AddConsumer<ProductDeleteConsumer>();
    x.AddConsumer<ProductUpdateConsumer>();
    x.AddConsumer<SyncProductToElasticConsumer>();

    x.UsingRabbitMq((context, cfg) =>
    {
        cfg.UseMessageRetry(r => r.Incremental(3, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2)));
        cfg.Host(new Uri(builder.Configuration.GetConnectionString("rabbitmq") ?? "amqp://guest:guest@localhost:5672/"));

        cfg.ReceiveEndpoint("reserve-stock", e =>
        {
            e.UseEntityFrameworkOutbox<ProductDbContext>(context);
            e.ConfigureConsumer<ReserveStockConsumer>(context);
        });

        cfg.ReceiveEndpoint("release-stock", e =>
        {
            e.UseEntityFrameworkOutbox<ProductDbContext>(context);
            e.ConfigureConsumer<ReleaseStockConsumer>(context);
        });

        cfg.ReceiveEndpoint("product-creation", e =>
        {
            e.UseEntityFrameworkOutbox<ProductDbContext>(context);
            e.ConfigureConsumer<ProductCreationConsumer>(context);
        });

        cfg.ReceiveEndpoint("product-delete", e =>
        {
            e.UseEntityFrameworkOutbox<ProductDbContext>(context);
            e.ConfigureConsumer<ProductDeleteConsumer>(context);
        });

        cfg.ReceiveEndpoint("product-update", e =>
        {
            e.UseEntityFrameworkOutbox<ProductDbContext>(context);
            e.ConfigureConsumer<ProductUpdateConsumer>(context);
        });

        cfg.ReceiveEndpoint("elastic-search", e =>
        {
            e.ConcurrentMessageLimit = 2;
            e.ConfigureConsumer<SyncProductToElasticConsumer>(context);
        });
    });
});

// ── Add Redis ────────────────────────────────────────────────────────────────
builder.AddRedisClient("redis");
builder.Services.AddRedisIdempotency();

builder.Services.AddHttpClient<IAiEmbeddingService, LocalEmbeddingService>();

var app = builder.Build();

await app.MigrateDatabaseAsync<ProductDbContext>();

// ── Middleware ────────────────────────────────────────────────────────────────
app.UseApiServiceDefaults();

app.UseAuthentication();
app.UseAuthorization();

// ── Endpoints ─────────────────────────────────────────────────────────────────
app.MapHealthChecks("/health");

using (var scope = app.Services.CreateScope())
{
    var elasticClient = scope.ServiceProvider.GetRequiredService<ElasticsearchClient>();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();

    // Gọi hàm cấu hình
    await ElasticConfigurator.SetupIndexAsync(elasticClient, logger);
}
app.MapProductEndpoints();

app.Run();
