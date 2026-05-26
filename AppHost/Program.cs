using Projects;

var builder = DistributedApplication.CreateBuilder(args);

var postgresUserName = builder.AddParameter("postgres-username");
var postgresPassword = builder.AddParameter("postgres-password", secret: true);
var rabbitMqUserName = builder.AddParameter("rabbitmq-username");
var rabbitMqPassword = builder.AddParameter("rabbitmq-password", secret: true);

// ── External Resources ────────────────────────────────────────────────────────
var postgres = builder.AddPostgres("postgres", postgresUserName, postgresPassword, port: 5432)
    .WithDataVolume()
    .WithPgAdmin(pgAdmin => pgAdmin.WithHostPort(5050));

var rabbitmq = builder.AddRabbitMQ("rabbitmq", rabbitMqUserName, rabbitMqPassword, port: 5672)
    .WithManagementPlugin(port: 15672);

var redis = builder.AddRedis("redis");

var elasticsearch = builder.AddElasticsearch("elasticsearch")
    .WithDataVolume();

var infinityApi = builder.AddContainer("infinity-ai", "michaelf34/infinity")
    .WithImageTag("latest")
    .WithHttpEndpoint(port: 7997, targetPort: 7997, name: "api")
    .WithEnvironment("MODEL_ID", "BAAI/bge-m3") 
    .WithEnvironment("PORT", "7997");
     //.WithEnvironment("DEVICE", "cuda");

// ── Databases (mỗi service dùng DB riêng) ────────────────────────────────────
var orderDb = postgres.AddDatabase("order-db");
var productDb = postgres.AddDatabase("product-db");
var identityDb = postgres.AddDatabase("identity-db");
var paymentDb = postgres.AddDatabase("payment-db");
var notificationDb = postgres.AddDatabase("notification-db");

// ── Services ──────────────────────────────────────────────────────────────────
var orderApi = builder.AddProject<Order_API>("order-api")
    .WithReference(orderDb)
    .WithReference(rabbitmq)
    .WithReference(redis)
    .WaitFor(orderDb)
    .WaitFor(rabbitmq)
    .WaitFor(redis);

var cartApi = builder.AddProject<Cart_API>("cart-api")
    .WithReference(rabbitmq)
    .WithReference(redis)
    .WaitFor(rabbitmq)
    .WaitFor(redis);

var productApi = builder.AddProject<Product_API>("product-api")
    .WithReference(productDb)
    .WithReference(rabbitmq)
    .WithReference(redis)
    .WithReference(elasticsearch)
    .WithReference(infinityApi.GetEndpoint("api")).WaitFor(productDb)
    .WaitFor(rabbitmq)
    .WaitFor(redis)
    .WaitFor(elasticsearch);    

var identityApi = builder.AddProject<Identity_API>("identity-api")
    .WithReference(identityDb)
    .WithReference(rabbitmq)
    .WithReference(redis)
    .WaitFor(identityDb)
    .WaitFor(rabbitmq)
    .WaitFor(redis);

var paymentApi = builder.AddProject<Payment_API>("payment-api")
    .WithReference(paymentDb)
    .WithReference(rabbitmq)
    .WithReference(redis)
    .WaitFor(paymentDb)
    .WaitFor(rabbitmq)
    .WaitFor(redis);

var notificationApi = builder.AddProject<Notification_API>("notification-api")
    .WithReference(notificationDb)
    .WithReference(rabbitmq)
    .WithReference(redis)
    .WaitFor(notificationDb)
    .WaitFor(rabbitmq)
    .WaitFor(redis);

var gateway = builder.AddProject<ApiGateway>("api-gateway")
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
