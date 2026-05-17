using EventBus.Extensions;
using EventBus.Infrastructure;
using MassTransit;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi;
using Product.API.Endpoints;
using Product.API.IntegrationEvents.Consumers.OrderSupportConsumer;
using Product.API.IntegrationEvents.Consumers.Self;
using Product.Infrastructure.Data;

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
builder.Services.AddProblemDetails();
builder.Services.AddHealthChecks();
builder.Services.AddHttpLogging();

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
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options =>
{
    var jwtSection = builder.Configuration.GetSection("Jwt");
    var key = jwtSection["Key"];

    if (string.IsNullOrWhiteSpace(key))
    {
        if (!builder.Environment.IsDevelopment())
            throw new InvalidOperationException("Jwt:Key not configured");

        key = "development-only-product-service-signing-key-please-use-user-secrets";
    }

    options.TokenValidationParameters = new Microsoft.IdentityModel.Tokens.TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = jwtSection["Issuer"],
        ValidAudience = jwtSection["Audience"],
        IssuerSigningKey = new Microsoft.IdentityModel.Tokens.SymmetricSecurityKey(
            System.Text.Encoding.UTF8.GetBytes(key))
    };
});
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

    x.AddConsumer<OrderCreatedConsumer>();
    x.AddConsumer<OrderCancelledConsumer>();
    x.AddConsumer<OrderReturnedConsumer>();
    x.AddConsumer<ProductCreationConsumer>();
    x.AddConsumer<ProductDeleteConsumer>();
    x.AddConsumer<ProductUpdateConsumer>();

    x.UsingRabbitMq((context, cfg) =>
    {
        cfg.UseMessageRetry(r => r.Incremental(3, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2)));
        cfg.Host(new Uri(builder.Configuration.GetConnectionString("rabbitmq") ?? "amqp://guest:guest@localhost:5672/"));

        cfg.ReceiveEndpoint("order-created", e =>
        {
            e.UseEntityFrameworkOutbox<ProductDbContext>(context);
            e.ConfigureConsumer<OrderCreatedConsumer>(context);
        });

        cfg.ReceiveEndpoint("order-cancelled", e =>
        {
            e.UseEntityFrameworkOutbox<ProductDbContext>(context);
            e.ConfigureConsumer<OrderCancelledConsumer>(context);
        });

        cfg.ReceiveEndpoint("order-returned", e =>
        {
            e.UseEntityFrameworkOutbox<ProductDbContext>(context);
            e.ConfigureConsumer<OrderReturnedConsumer>(context);
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
    });
});

// ── Add Redis ────────────────────────────────────────────────────────────────
var redisConnectionString = builder.Configuration.GetConnectionString("redis") ??
                            builder.Configuration.GetConnectionString("DefaultConnection") ??
                            throw new InvalidOperationException("Missing connection string for redis");
builder.Services.AddRedisIdempotency(redisConnectionString);

var app = builder.Build();

await app.MigrateDatabaseAsync<ProductDbContext>();

// ── Middleware ────────────────────────────────────────────────────────────────
app.UseExceptionHandler();
app.UseHttpLogging();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "Product API v1"));
}

app.UseAuthentication();
app.UseAuthorization();

// ── Endpoints ─────────────────────────────────────────────────────────────────
app.MapHealthChecks("/health");
app.MapProductEndpoints();

app.Run();
