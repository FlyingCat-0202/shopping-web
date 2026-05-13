using Projects;

var builder = DistributedApplication.CreateBuilder(args);

var postgresUserName = builder.AddParameter("postgres-username", secret: false);
var postgresPassword = builder.AddParameter("postgres-password", secret: true);
var rabbitMqUserName = builder.AddParameter("rabbitmq-username", secret: false);
var rabbitMqPassword = builder.AddParameter("rabbitmq-password", secret: true);

// ── External Resources ────────────────────────────────────────────────────────
var postgres = builder.AddPostgres("postgres", postgresUserName, postgresPassword, port: 5432)
    .WithDataVolume()
    .WithPgAdmin(pgAdmin => pgAdmin.WithHostPort(5050));

var rabbitmq = builder.AddRabbitMQ("rabbitmq", rabbitMqUserName, rabbitMqPassword, port: 5672)
    .WithManagementPlugin(port: 15672);

// ── Databases (mỗi service dùng DB riêng) ────────────────────────────────────
var orderDb = postgres.AddDatabase("order-db");
var cartDb = postgres.AddDatabase("cart-db");
var productDb = postgres.AddDatabase("product-db");

// ── Services ──────────────────────────────────────────────────────────────────
builder.AddProject<Order_API>("order-api")
    .WithReference(orderDb)
    .WithReference(rabbitmq)
    .WaitFor(orderDb)
    .WaitFor(rabbitmq);

builder.AddProject<Cart_API>("cart-api")
    .WithReference(cartDb)
    .WithReference(rabbitmq)
    .WithEnvironment("Services__ProductServiceUrl", "http://localhost:5133")
    .WaitFor(cartDb)
    .WaitFor(rabbitmq);

builder.AddProject<Product_API>("product-api")
    .WithReference(productDb)
    .WithReference(rabbitmq)
    .WaitFor(productDb)
    .WaitFor(rabbitmq);

builder.Build().Run();
