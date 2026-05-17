using Cart.API.CartStore;
using Cart.API.IntegrationEvents.Consumers;
using Cart.API.Endpoints;
using Cart.API.Validators;
using EventBus.Infrastructure;
using FluentValidation;
using MassTransit;
using Microsoft.OpenApi;
using ServiceDefault;

var builder = WebApplication.CreateBuilder(args);

builder.AddApiServiceDefaults();

builder.Services.AddValidatorsFromAssemblyContaining<CartItemValidator>();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "Cart API", Version = "v1" });

    var bearerSchemeId = "Bearer";
    c.AddSecurityDefinition(bearerSchemeId, new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT"
    });
    c.AddSecurityRequirement(document => new OpenApiSecurityRequirement
    {
        [new OpenApiSecuritySchemeReference(bearerSchemeId, document)] = []
    });
});

builder.Services.AddJwtAuthentication(builder.Configuration);
builder.Services.AddAuthorization();

builder.Services.AddMassTransit(x =>
{
    x.SetKebabCaseEndpointNameFormatter();

    x.AddConsumer<CartItemsRemovedConsumer>();

    x.UsingRabbitMq((context, cfg) =>
    {
        cfg.UseMessageRetry(r => r.Incremental(3, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2)));
        cfg.Host(new Uri(builder.Configuration.GetConnectionString("rabbitmq") ?? "amqp://guest:guest@localhost:5672/"));

        cfg.ReceiveEndpoint("cart-items-removed", e =>
        {
            e.ConfigureConsumer<CartItemsRemovedConsumer>(context);
        });
    });
});

// ── Add Redis ────────────────────────────────────────────────────────────────
var redisConnectionString = builder.Configuration.GetConnectionString("redis") ??
                            builder.Configuration.GetConnectionString("DefaultConnection") ??
                            throw new InvalidOperationException("Missing connection string for redis");
builder.Services.AddRedisIdempotency(redisConnectionString);
builder.Services.AddSingleton<ICartStore, RedisCartStore>();

var app = builder.Build();

app.UseApiServiceDefaults();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "Cart API v1"));
}

app.UseAuthentication();
app.UseAuthorization();

app.MapHealthChecks("/health");
app.MapCartEndpoints();

app.Run();
