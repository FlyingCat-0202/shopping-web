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

// ── Databases (mỗi service dùng DB riêng) ────────────────────────────────────
var orderDb = postgres.AddDatabase("order-db");
var productDb = postgres.AddDatabase("product-db");
var identityDb = postgres.AddDatabase("identity-db");
var paymentDb = postgres.AddDatabase("payment-db");

// ── Services ──────────────────────────────────────────────────────────────────
builder.AddProject<Order_API>("order-api")
    .WithReference(orderDb)
    .WithReference(rabbitmq)
    .WithReference(redis)
    .WaitFor(orderDb)
    .WaitFor(rabbitmq)
    .WaitFor(redis);

builder.AddProject<Cart_API>("cart-api")
    .WithReference(rabbitmq)
    .WithReference(redis)
    .WaitFor(rabbitmq)
    .WaitFor(redis);

builder.AddProject<Product_API>("product-api")
    .WithReference(productDb)
    .WithReference(rabbitmq)
    .WithReference(redis)
    .WaitFor(productDb)
    .WaitFor(rabbitmq)
    .WaitFor(redis);

builder.AddProject<Identity_API>("identity-api")
    .WithReference(identityDb)
    .WithReference(rabbitmq)
    .WithReference(redis)
    .WaitFor(identityDb)
    .WaitFor(rabbitmq)
    .WaitFor(redis);

builder.AddProject<Payment_API>("payment-api")
    .WithReference(paymentDb)
    .WithReference(rabbitmq)
    .WithReference(redis)
    .WaitFor(paymentDb)
    .WaitFor(rabbitmq)
    .WaitFor(redis);

builder.Build().Run();
