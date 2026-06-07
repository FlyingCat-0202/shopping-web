using Cart.API.CartStore;
using Cart.API.IntegrationEvents.Consumers;
using Cart.API.Endpoints;
using Cart.API.Validators;
using EventBus.Infrastructure;
using FluentValidation;
using MassTransit;
using ServiceDefault;

var builder = WebApplication.CreateBuilder(args);

builder.AddApiServiceDefaults();

builder.Services.AddValidatorsFromAssemblyContaining<CartItemValidator>();

builder.Services.AddJwtAuthentication(builder.Configuration);
builder.Services.AddAuthorization();

builder.Services.AddMassTransit(x =>
{
    x.SetKebabCaseEndpointNameFormatter();

    x.AddConsumer<RemoveCartItemsConsumer>();

    x.UsingRabbitMq((context, cfg) =>
    {
        cfg.UseMessageRetry(r => r.Incremental(3, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2)));
        cfg.Host(builder.Configuration.GetRequiredConnectionStringUri("rabbitmq"));

        cfg.ReceiveEndpoint("remove-cart-items", e =>
        {
            e.ConfigureConsumer<RemoveCartItemsConsumer>(context);
        });
    });
});

// ── Add Redis ────────────────────────────────────────────────────────────────
builder.AddRedisClient("redis");
builder.Services.AddRedisIdempotency();
builder.Services.AddSingleton<ICartStore, RedisCartStore>();

var app = builder.Build();

app.UseApiServiceDefaults();

app.UseAuthentication();
app.UseAuthorization();

app.MapApiHealthChecks();
app.MapCartEndpoints();

app.Run();
