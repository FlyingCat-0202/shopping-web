using Cart.API.Clients;
using Cart.API.IntegrationEvents.Consumers;
using Cart.API.Endpoints;
using Cart.API.Validators;
using Cart.Infrastructure.Data;
using Cart.Infrastructure.Idempotency;
using EventBus.Infrastructure;
using FluentValidation;
using MassTransit;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddProblemDetails();
builder.Services.AddHealthChecks();
builder.Services.AddHttpLogging();

builder.Services.AddDbContext<CartDbContext>(options =>
{
    options.UseNpgsql(
        builder.Configuration.GetConnectionString("cart-db")
            ?? builder.Configuration.GetConnectionString("DefaultConnection")
            ?? "Host=localhost;Port=5432;Database=cart-db;Username=postgres;Password=postgres",
        npgsql =>
        {
            npgsql.MigrationsHistoryTable("__EFMigrationsHistory_Cart", "cart");
            npgsql.EnableRetryOnFailure(
                maxRetryCount: 5,
                maxRetryDelay: TimeSpan.FromSeconds(30),
                errorCodesToAdd: null);
        });
});

builder.Services.AddValidatorsFromAssemblyContaining<CartItemValidator>();

builder.Services.AddHttpContextAccessor();
builder.Services.AddHttpClient<IProductCatalogClient, ProductCatalogClient>((serviceProvider, client) =>
{
    var configuration = serviceProvider.GetRequiredService<IConfiguration>();
    var productServiceUrl = configuration["Services:ProductServiceUrl"] ?? "http://localhost:5133";
    client.BaseAddress = new Uri(productServiceUrl);
});

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
builder.Services.AddScoped<IIdempotencyService, CartIdempotencyService>();

builder.Services.AddMassTransit(x =>
{
    x.SetKebabCaseEndpointNameFormatter();

    x.AddConsumer<CartItemsRemovedConsumer>();
    x.AddConsumer<ProductDeletedConsumer>();

    x.UsingRabbitMq((context, cfg) =>
    {
        cfg.UseMessageRetry(r => r.Incremental(3, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2)));
        cfg.Host(new Uri(builder.Configuration.GetConnectionString("rabbitmq") ?? "amqp://guest:guest@localhost:5672/"));

        cfg.ReceiveEndpoint("cart-items-removed", e =>
        {
            e.ConfigureConsumer<CartItemsRemovedConsumer>(context);
        });

        cfg.ReceiveEndpoint("product-deleted-cart", e =>
        {
            e.ConfigureConsumer<ProductDeletedConsumer>(context);
        });
    });
});

var app = builder.Build();

app.UseExceptionHandler();
app.UseHttpLogging();

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
