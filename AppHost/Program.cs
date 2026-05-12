using Projects;

var builder = DistributedApplication.CreateBuilder(args);

// ── External Resources ────────────────────────────────────────────────────────
var postgres = builder.AddPostgres("postgres")
    .WithDataVolume()
    .WithPgAdmin();

var rabbitmq = builder.AddRabbitMQ("rabbitmq")
    .WithManagementPlugin();

// ── Databases (mỗi service dùng DB riêng) ────────────────────────────────────
var orderDb = postgres.AddDatabase("order-db");
var cartDb = postgres.AddDatabase("cart-db");

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

builder.Build().Run();
