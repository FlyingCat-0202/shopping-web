using Projects;
using System.Security.Cryptography;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

var builder = DistributedApplication.CreateBuilder(args);

var postgresUserName = builder.AddParameter("postgres-username");
var postgresPassword = builder.AddParameter("postgres-password", secret: true);
var rabbitMqUserName = builder.AddParameter("rabbitmq-username");
var rabbitMqPassword = builder.AddParameter("rabbitmq-password", secret: true);
var (jwtPrivateKey, jwtPublicKey) = ResolveJwtKeys(builder);
var paymentWebhookSecret = ResolvePaymentWebhookSecret(builder);
var infinityAiDevice = builder.Configuration["Parameters:infinity-ai-device"]
    ?? builder.Configuration["INFINITY_AI_DEVICE"]
    ?? "cpu";
var infinityAiModel = builder.Configuration["Parameters:infinity-ai-model"]
    ?? builder.Configuration["INFINITY_AI_MODEL"]
    ?? "BAAI/bge-m3";

// ── External Resources ────────────────────────────────────────────────────────
var postgres = builder.AddPostgres("postgres", postgresUserName, postgresPassword, port: 5432)
    .WithImageTag("17-alpine")
    .WithDataVolume()
    .WithPgAdmin(pgAdmin => pgAdmin.WithHostPort(5050));

var rabbitmq = builder.AddRabbitMQ("rabbitmq", rabbitMqUserName, rabbitMqPassword, port: 5672)
    .WithManagementPlugin(port: 15672);

var redis = builder.AddRedis("redis");

var elasticsearch = builder.AddElasticsearch("elasticsearch")
    .WithDataVolume();

var infinityApi = builder.AddContainer("infinity-ai", "michaelf34/infinity")
    .WithImageTag("0.0.77")
    .WithHttpEndpoint(port: 7997, targetPort: 7997, name: "api")
    .WithHttpHealthCheck("/health", endpointName: "api")
    .WithEnvironment("MODEL_ID", infinityAiModel)
    .WithEnvironment("PORT", "7997")
    .WithEnvironment("DEVICE", infinityAiDevice);

if (string.Equals(infinityAiDevice, "cuda", StringComparison.OrdinalIgnoreCase))
{
    infinityApi.WithContainerRuntimeArgs("--gpus", "all");
}

// ── Databases (mỗi service dùng DB riêng) ────────────────────────────────────
var orderDb = postgres.AddDatabase("order-db");
var productDb = postgres.AddDatabase("product-db");
var identityDb = postgres.AddDatabase("identity-db");
var paymentDb = postgres.AddDatabase("payment-db");
var notificationDb = postgres.AddDatabase("notification-db");

// ── Services ──────────────────────────────────────────────────────────────────
var orderApi = builder.AddProject<Order_API>("order-api")
    .WithEnvironment("DOTNET_ENVIRONMENT", builder.Environment.EnvironmentName)
    .WithEnvironment("ASPNETCORE_ENVIRONMENT", builder.Environment.EnvironmentName)
    .WithEnvironment("Jwt__PublicKey", jwtPublicKey)
    .WithReference(orderDb)
    .WithReference(rabbitmq)
    .WithReference(redis)
    .WaitFor(orderDb)
    .WaitFor(rabbitmq)
    .WaitFor(redis);

var cartApi = builder.AddProject<Cart_API>("cart-api")
    .WithEnvironment("DOTNET_ENVIRONMENT", builder.Environment.EnvironmentName)
    .WithEnvironment("ASPNETCORE_ENVIRONMENT", builder.Environment.EnvironmentName)
    .WithEnvironment("Jwt__PublicKey", jwtPublicKey)
    .WithReference(rabbitmq)
    .WithReference(redis)
    .WaitFor(rabbitmq)
    .WaitFor(redis);

var productApi = builder.AddProject<Product_API>("product-api")
    .WithEnvironment("DOTNET_ENVIRONMENT", builder.Environment.EnvironmentName)
    .WithEnvironment("ASPNETCORE_ENVIRONMENT", builder.Environment.EnvironmentName)
    .WithEnvironment("Jwt__PublicKey", jwtPublicKey)
    .WithEnvironment("Embedding__ModelId", infinityAiModel)
    .WithReference(productDb)
    .WithReference(rabbitmq)
    .WithReference(redis)
    .WithReference(elasticsearch)
    .WithReference(infinityApi.GetEndpoint("api"))
    .WaitFor(productDb)
    .WaitFor(rabbitmq)
    .WaitFor(redis)
    .WaitFor(elasticsearch)
    .WaitFor(infinityApi);

var identityApi = builder.AddProject<Identity_API>("identity-api")
    .WithEnvironment("DOTNET_ENVIRONMENT", builder.Environment.EnvironmentName)
    .WithEnvironment("ASPNETCORE_ENVIRONMENT", builder.Environment.EnvironmentName)
    .WithEnvironment("Jwt__PrivateKey", jwtPrivateKey)
    .WithEnvironment("Jwt__PublicKey", jwtPublicKey)
    .WithReference(identityDb)
    .WithReference(rabbitmq)
    .WithReference(redis)
    .WaitFor(identityDb)
    .WaitFor(rabbitmq)
    .WaitFor(redis);

ConfigureOptionalAdminSeed(identityApi, builder.Configuration);

var paymentApi = builder.AddProject<Payment_API>("payment-api")
    .WithEnvironment("DOTNET_ENVIRONMENT", builder.Environment.EnvironmentName)
    .WithEnvironment("ASPNETCORE_ENVIRONMENT", builder.Environment.EnvironmentName)
    .WithEnvironment("Jwt__PublicKey", jwtPublicKey)
    .WithEnvironment("Payment__WebhookSecret", paymentWebhookSecret)
    .WithReference(paymentDb)
    .WithReference(rabbitmq)
    .WithReference(redis)
    .WaitFor(paymentDb)
    .WaitFor(rabbitmq)
    .WaitFor(redis);

var notificationApi = builder.AddProject<Notification_API>("notification-api")
    .WithEnvironment("DOTNET_ENVIRONMENT", builder.Environment.EnvironmentName)
    .WithEnvironment("ASPNETCORE_ENVIRONMENT", builder.Environment.EnvironmentName)
    .WithEnvironment("Jwt__PublicKey", jwtPublicKey)
    .WithReference(notificationDb)
    .WithReference(rabbitmq)
    .WithReference(redis)
    .WaitFor(notificationDb)
    .WaitFor(rabbitmq)
    .WaitFor(redis);

var gateway = builder.AddProject<ApiGateway>("api-gateway")
    .WithEnvironment("DOTNET_ENVIRONMENT", builder.Environment.EnvironmentName)
    .WithEnvironment("ASPNETCORE_ENVIRONMENT", builder.Environment.EnvironmentName)
    .WithHttpEndpoint(port: 5000, targetPort: 5000, name: "http", isProxied: false)
    .WithReference(identityApi)
    .WithReference(productApi)
    .WithReference(cartApi)
    .WithReference(orderApi)
    .WithReference(paymentApi)
    .WithReference(notificationApi)
    .WaitFor(identityApi)
    .WaitFor(productApi)
    .WaitFor(cartApi)
    .WaitFor(orderApi)
    .WaitFor(paymentApi)
    .WaitFor(notificationApi);

builder.AddNpmApp("web-store-angular", "../src/Web/web-store-angular", scriptName: "start:aspire")
    .WithHttpEndpoint(port: 4200, targetPort: 4200, name: "http", isProxied: false)
    .WaitFor(gateway);

builder.Build().Run();

static (string PrivateKey, string PublicKey) ResolveJwtKeys(IDistributedApplicationBuilder builder)
{
    var privateKey = builder.Configuration["Jwt:PrivateKey"];
    var publicKey = builder.Configuration["Jwt:PublicKey"];

    if (!string.IsNullOrWhiteSpace(privateKey) && !string.IsNullOrWhiteSpace(publicKey))
        return (privateKey, publicKey);

    if (builder.Environment.IsProduction())
    {
        throw new InvalidOperationException(
            "Jwt:PrivateKey and Jwt:PublicKey must be supplied through a production secret store.");
    }

    using var rsa = RSA.Create(2048);
    return (rsa.ExportPkcs8PrivateKeyPem(), rsa.ExportSubjectPublicKeyInfoPem());
}

static string ResolvePaymentWebhookSecret(IDistributedApplicationBuilder builder)
{
    var secret = builder.Configuration["Payment:WebhookSecret"];
    if (!string.IsNullOrWhiteSpace(secret))
        return secret;

    if (builder.Environment.IsProduction())
    {
        throw new InvalidOperationException(
            "Payment:WebhookSecret must be supplied through a production secret store.");
    }

    return Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
}

static void ConfigureOptionalAdminSeed(
    IResourceBuilder<ProjectResource> identityApi,
    IConfiguration configuration)
{
    var email = configuration["SeedAdmin:Email"];
    var password = configuration["SeedAdmin:Password"];

    if (string.IsNullOrWhiteSpace(email) && string.IsNullOrWhiteSpace(password))
        return;

    if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
    {
        throw new InvalidOperationException(
            "SeedAdmin:Email and SeedAdmin:Password must either both be configured or both be omitted.");
    }

    identityApi
        .WithEnvironment("SeedAdmin__Email", email)
        .WithEnvironment("SeedAdmin__Password", password);

    AddOptionalEnvironment(identityApi, configuration, "SeedAdmin:FullName", "SeedAdmin__FullName");
    AddOptionalEnvironment(identityApi, configuration, "SeedAdmin:PhoneNumber", "SeedAdmin__PhoneNumber");
}

static void AddOptionalEnvironment(
    IResourceBuilder<ProjectResource> resource,
    IConfiguration configuration,
    string configurationKey,
    string environmentKey)
{
    var value = configuration[configurationKey];
    if (!string.IsNullOrWhiteSpace(value))
        resource.WithEnvironment(environmentKey, value);
}
