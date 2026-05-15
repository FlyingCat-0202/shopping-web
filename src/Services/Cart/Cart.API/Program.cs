using Cart.API.CartStore;
using Cart.API.Idempotency;
using Cart.API.IntegrationEvents.Consumers;
using Cart.API.Endpoints;
using Cart.API.Validators;
using EventBus.Extensions;
using EventBus.Infrastructure;
using FluentValidation;
using MassTransit;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using StackExchange.Redis;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddProblemDetails();
builder.Services.AddHealthChecks();
builder.Services.AddHttpLogging();
builder.Services.AddFrontendCors(builder.Configuration, builder.Environment);

builder.Services.AddValidatorsFromAssemblyContaining<CartItemValidator>();
builder.Services.AddSingleton<IConnectionMultiplexer>(_ =>
{
    var connectionString = builder.Configuration.GetConnectionString("redis") ?? "localhost:6379";
    var options = ConfigurationOptions.Parse(connectionString);
    options.AbortOnConnectFail = false;
    return ConnectionMultiplexer.Connect(options);
});
builder.Services.AddSingleton<ICartStore, RedisCartStore>();

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

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options =>
{
    var jwtSection = builder.Configuration.GetSection("Jwt");
    var key = jwtSection["Key"];

    if (string.IsNullOrWhiteSpace(key))
    {
        if (!builder.Environment.IsDevelopment())
            throw new InvalidOperationException("Jwt:Key not configured");

        key = "development-only-cart-service-signing-key-please-use-user-secrets";
    }

    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = jwtSection["Issuer"],
        ValidAudience = jwtSection["Audience"],
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key))
    };
});
builder.Services.AddAuthorization();
builder.Services.AddSingleton<IIdempotencyService, RedisCartIdempotencyService>();

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

var app = builder.Build();

app.UseExceptionHandler();
app.UseHttpLogging();
app.UseFrontendCors();

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
