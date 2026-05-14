using Projects;

var builder = DistributedApplication.CreateBuilder(args);

var postgresUserName = builder.AddParameter("postgres-username", "myuser", publishValueAsDefault: false, secret: false);
var postgresPassword = builder.AddParameter("postgres-password", "pnthuc1609", publishValueAsDefault: false, secret: true);
var rabbitMqUserName = builder.AddParameter("rabbitmq-username", "guest", publishValueAsDefault: false, secret: false);
var rabbitMqPassword = builder.AddParameter("rabbitmq-password", "guest", publishValueAsDefault: false, secret: true);

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

// ── Services ──────────────────────────────────────────────────────────────────
builder.AddProject<Order_API>("order-api")
    .WithReference(orderDb)
    .WithReference(rabbitmq)
    .WaitFor(orderDb)
    .WaitFor(rabbitmq);

builder.AddProject<Cart_API>("cart-api")
    .WithReference(rabbitmq)
    .WithReference(redis)
    .WaitFor(rabbitmq)
    .WaitFor(redis);

builder.AddProject<Product_API>("product-api")
    .WithReference(productDb)
    .WithReference(rabbitmq)
    .WaitFor(productDb)
    .WaitFor(rabbitmq);

builder.AddProject<Identity_API>("identity-api")
    .WithReference(identityDb)
    .WithReference(rabbitmq)
    .WaitFor(identityDb)
    .WaitFor(rabbitmq);

builder.Build().Run();
