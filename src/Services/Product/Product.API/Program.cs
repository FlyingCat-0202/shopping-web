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

    // Batch consumer: gộp tối đa 100 ProductCreatedEvent, chờ tối đa 2 giây.
    // MassTransit tự động nhóm message từ queue vào Batch<T> trước khi gọi Consume().
    x.AddConsumer<SyncProductToElasticConsumer>(cfg =>
    {
        cfg.Options<BatchOptions>(o => o
            .SetMessageLimit(100)       // tối đa 100 message/batch
            .SetTimeLimit(TimeSpan.FromSeconds(2))); // hoặc flush sau 2 giây nếu chưa đủ 100
    });

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
            // ── Batch Consumer config ────────────────────────────────────────────────
            // PrefetchCount: kéo sẵn 2 × batch size message từ broker vào buffer.
            // ConcurrentMessageLimit: số batch được xử lý song song.
            //   → 2 batch đồng thời × 100 msg = 200 msg in-flight cùng lúc.
            //   → Mỗi batch: 1 AI call + 1 ES Bulk call = rất nhẹ.
            // ────────────────────────────────────────────────────────────────────────
            e.PrefetchCount = 200;
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
    var db = scope.ServiceProvider.GetRequiredService<ProductDbContext>();
    var aiEmbeddingService = scope.ServiceProvider.GetRequiredService<IAiEmbeddingService>();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();

    // Gọi hàm cấu hình
    var createdIndex = await ElasticConfigurator.SetupIndexAsync(elasticClient, logger);
    if (createdIndex)
    {
        await ElasticConfigurator.RebuildIndexFromDatabaseAsync(db, elasticClient, aiEmbeddingService, logger);
    }
}
app.MapProductEndpoints();

app.Run();
