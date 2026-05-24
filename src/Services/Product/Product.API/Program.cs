using Elastic.Clients.Elasticsearch;
using EventBus.Extensions;
using EventBus.Infrastructure;
using FluentValidation;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi;
using Product.API.Endpoints;
using Product.API.IntegrationEvents.Consumers.Elastic;
using Product.API.IntegrationEvents.Consumers.OrderSupportConsumer;
using Product.API.IntegrationEvents.Consumers.Self;
using Product.API.Validators;
using Product.Domain.Entities;
using Product.Infrastructure.Data;
using Product.Infrastructure.Gemini;
using ServiceDefault;

var builder = WebApplication.CreateBuilder(args);

// ── DbContext ────────────────────────────────────────────────────────────────
builder.Services.AddDbContext<ProductDbContext>(options =>
{
    options.UseNpgsql(
        builder.Configuration.GetConnectionString("product-db")
            ?? builder.Configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException(
                "Missing connection string. Set ConnectionStrings__product-db or run the service through Aspire AppHost."),
        npgsql =>
        {
            npgsql.MigrationsHistoryTable("__EFMigrationsHistory_Product", "product");
            npgsql.EnableRetryOnFailure(
                maxRetryCount: 5,
                maxRetryDelay: TimeSpan.FromSeconds(30),
                errorCodesToAdd: null);
        });
});

// ── Infrastructure ────────────────────────────────────────────────────────────
builder.AddApiServiceDefaults();
builder.Services.AddValidatorsFromAssemblyContaining<ProductRequestValidator>();
builder.Services.AddScoped<IStockReservationService, StockReservationService>();
// >>> THÊM ĐOẠN CODE NÀY VÀO ĐÂY <<<
var elasticUri = builder.Configuration.GetConnectionString("elasticsearch")
                 ?? "http://localhost:9200"; // Đổi port tùy theo Docker của bạn

var settings = new ElasticsearchClientSettings(new Uri(elasticUri))
    // .Authentication(new BasicAuthentication("elastic", "changeme")) // Bỏ comment nếu ES của bạn có cài password
    .DefaultIndex("products"); // (Tùy chọn) Đặt index mặc định để mốt khỏi phải gõ lại chuỗi "products"

builder.Services.AddSingleton(new ElasticsearchClient(settings));
// >>> KẾT THÚC ĐOẠN THÊM MỚI <<<

// ── Swagger / OpenAPI ─────────────────────────────────────────────────────────
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "Product API", Version = "v1" });
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
            e.ConfigureConsumer<SyncProductToElasticConsumer>(context);
        });
    });
});

// ── Add Redis ────────────────────────────────────────────────────────────────
var redisConnectionString = builder.Configuration.GetConnectionString("redis") ??
                            builder.Configuration.GetConnectionString("DefaultConnection") ??
                            throw new InvalidOperationException("Missing connection string for redis");
builder.Services.AddRedisIdempotency(redisConnectionString);

builder.Services.AddHttpClient<IAiEmbeddingService, GeminiEmbeddingService>();

var app = builder.Build();

await app.MigrateDatabaseAsync<ProductDbContext>();
//await ProductSeedData.SeedAsync(app);

// ── Middleware ────────────────────────────────────────────────────────────────
app.UseApiServiceDefaults();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "Product API v1"));
}

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
