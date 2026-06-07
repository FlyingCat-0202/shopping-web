using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Payment.Infrastructure.Data;
using System.Security.Cryptography;
using Testcontainers.PostgreSql;
using Testcontainers.RabbitMq;

namespace Payment.ApiTests;

public sealed class PaymentApiFactory : WebApplicationFactory<Program>
{
    public const string WebhookSecret = "payment-api-tests-secret";
    private static readonly string JwtPublicKey = CreateJwtPublicKey();

    private readonly PostgreSqlContainer _postgres;
    private readonly RabbitMqContainer _rabbitMq;
    private readonly IContainer _redis;

    private PaymentApiFactory(PostgreSqlContainer postgres, RabbitMqContainer rabbitMq, IContainer redis)
    {
        _postgres = postgres;
        _rabbitMq = rabbitMq;
        _redis = redis;
    }

    public static bool IsDockerAvailable()
    {
        try
        {
            var container = CreateRedisContainer();
            container.DisposeAsync().AsTask().GetAwaiter().GetResult();
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static async Task<PaymentApiFactory> StartAsync()
    {
        var postgres = CreatePostgresContainer();
        var rabbitMq = CreateRabbitMqContainer();
        var redis = CreateRedisContainer();

        await postgres.StartAsync();
        await rabbitMq.StartAsync();
        await redis.StartAsync();

        var redisConnectionString = $"{redis.Hostname}:{redis.GetMappedPublicPort(6379)}";

        Environment.SetEnvironmentVariable("ConnectionStrings__payment-db", postgres.GetConnectionString());
        Environment.SetEnvironmentVariable("ConnectionStrings__rabbitmq", rabbitMq.GetConnectionString());
        Environment.SetEnvironmentVariable("ConnectionStrings__redis", redisConnectionString);
        Environment.SetEnvironmentVariable("Payment__WebhookSecret", WebhookSecret);
        Environment.SetEnvironmentVariable("Jwt__PublicKey", JwtPublicKey);

        var factory = new PaymentApiFactory(postgres, rabbitMq, redis);
        await factory.InitializeDatabaseAsync();

        return factory;
    }

    public async Task SeedAsync(Action<PaymentDbContext> seed)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PaymentDbContext>();
        seed(db);
        await db.SaveChangesAsync();
    }

    public async Task<T> QueryAsync<T>(Func<PaymentDbContext, Task<T>> query)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PaymentDbContext>();
        return await query(db);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:payment-db"] = _postgres.GetConnectionString(),
                ["ConnectionStrings:rabbitmq"] = _rabbitMq.GetConnectionString(),
                ["ConnectionStrings:redis"] = $"{_redis.Hostname}:{_redis.GetMappedPublicPort(6379)}",
                ["Payment:WebhookSecret"] = WebhookSecret,
                ["Jwt:PublicKey"] = JwtPublicKey
            });
        });

        builder.ConfigureServices(services =>
        {
            var dbContextDescriptors = services
                .Where(descriptor =>
                    descriptor.ServiceType == typeof(PaymentDbContext) ||
                    (descriptor.ServiceType.FullName?.Contains(nameof(PaymentDbContext), StringComparison.Ordinal) ?? false) ||
                    (descriptor.ServiceType.FullName?.Contains("DbContextOptions", StringComparison.Ordinal) ?? false) ||
                    (descriptor.ServiceType.FullName?.Contains("DbContextPool", StringComparison.Ordinal) ?? false) ||
                    (descriptor.ServiceType.FullName?.Contains("ScopedDbContextLease", StringComparison.Ordinal) ?? false))
                .ToList();

            foreach (var descriptor in dbContextDescriptors)
            {
                services.Remove(descriptor);
            }

            services.AddDbContextPool<PaymentDbContext>(options =>
            {
                options.UseNpgsql(_postgres.GetConnectionString(), npgsql =>
                {
                    npgsql.MigrationsHistoryTable("__EFMigrationsHistory_Payment", "payment");
                });
            });
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (disposing)
        {
            _redis.DisposeAsync().AsTask().GetAwaiter().GetResult();
            _rabbitMq.DisposeAsync().AsTask().GetAwaiter().GetResult();
            _postgres.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }

    private static PostgreSqlContainer CreatePostgresContainer()
        => new PostgreSqlBuilder()
            .WithImage("postgres:17-alpine")
            .WithDatabase("payment-db")
            .WithUsername("postgres")
            .WithPassword("postgres")
            .Build();

    private static RabbitMqContainer CreateRabbitMqContainer()
        => new RabbitMqBuilder()
            .WithImage("rabbitmq:4-management")
            .WithUsername("guest")
            .WithPassword("guest")
            .Build();

    private static IContainer CreateRedisContainer()
        => new ContainerBuilder()
            .WithImage("redis:7-alpine")
            .WithPortBinding(6379, true)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilInternalTcpPortIsAvailable(6379))
            .Build();

    private static string CreateJwtPublicKey()
    {
        using var rsa = RSA.Create(2048);
        return rsa.ExportSubjectPublicKeyInfoPem();
    }

    private async Task InitializeDatabaseAsync()
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PaymentDbContext>();
        await db.Database.MigrateAsync();
    }
}
