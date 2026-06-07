using EventBus.Extensions;
using EventBus.Infrastructure;
using FluentValidation;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Product.API.Catalog;
using Product.API.Endpoints;
using Product.API.IntegrationEvents.Consumers.OrderSupportConsumer;
using Product.API.IntegrationEvents.Consumers.Self;
using Product.API.Products;
using Product.API.Search;
using Product.API.Search.Indexing;
using Product.API.Validators;
using Product.Domain.Search;
using Product.Infrastructure.Search;
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
builder.Services.AddScoped<IProductElasticSearch, ProductElasticSearch>();
builder.Services.AddScoped<IProductDatabaseSearch, ProductDatabaseSearch>();
builder.Services.AddScoped<IProductSearchService, ProductSearchService>();
builder.Services.AddScoped<IProductCatalogService, ProductCatalogService>();
builder.Services.AddScoped<IProductReadService, ProductReadService>();
builder.Services.AddScoped<IProductAdminCommandService, ProductAdminCommandService>();
builder.Services.AddScoped<IProductMutationService, ProductMutationService>();
builder.Services.Configure<ProductSearchOptions>(
    builder.Configuration.GetSection(ProductSearchOptions.SectionName));
builder.Services.Configure<ProductCatalogOptions>(
    builder.Configuration.GetSection(ProductCatalogOptions.SectionName));
builder.Services.AddHostedService<ProductElasticIndexWorker>();
builder.AddElasticsearchClient(
    "elasticsearch",
    configureClientSettings: settings => settings.DefaultIndex(ElasticProductIndex.Name));

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
    x.AddConsumer<ProductElasticSyncConsumer>(cfg =>
    {
        cfg.Options<BatchOptions>(o => o
            .SetMessageLimit(100)       // tối đa 100 message/batch
            .SetTimeLimit(TimeSpan.FromSeconds(2))); // hoặc flush sau 2 giây nếu chưa đủ 100
    });

    x.UsingRabbitMq((context, cfg) =>
    {
        cfg.UseMessageRetry(r => r.Incremental(3, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2)));
        cfg.Host(builder.Configuration.GetRequiredConnectionStringUri("rabbitmq"));

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
            e.ConfigureConsumer<ProductElasticSyncConsumer>(context);
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
app.MapApiHealthChecks();

app.MapProductEndpoints();

app.Run();
