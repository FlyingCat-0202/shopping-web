using DotNet.Testcontainers.Builders;
using Identity.Infrastructure.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using Respawn;
using Respawn.Graph;
using Testcontainers.PostgreSql;
using Testcontainers.RabbitMq;

namespace Identity.ApiTests;

public sealed class IdentityApiFactory : WebApplicationFactory<Program>
{
    private readonly PostgreSqlContainer _postgres;
    private readonly RabbitMqContainer _rabbitMq;
    private Respawner? _respawner;

    private IdentityApiFactory(PostgreSqlContainer postgres, RabbitMqContainer rabbitMq)
    {
        _postgres = postgres;
        _rabbitMq = rabbitMq;
    }

    public static bool IsDockerAvailable()
    {
        try
        {
            var container = CreatePostgresContainer();
            container.DisposeAsync().AsTask().GetAwaiter().GetResult();
            return true;
        }
        catch (DockerUnavailableException)
        {
            return false;
        }
    }

    public static async Task<IdentityApiFactory> StartAsync()
    {
        var postgres = CreatePostgresContainer();
        var rabbitMq = CreateRabbitMqContainer();

        await postgres.StartAsync();
        await rabbitMq.StartAsync();

        Environment.SetEnvironmentVariable("ConnectionStrings__identity-db", postgres.GetConnectionString());
        Environment.SetEnvironmentVariable("ConnectionStrings__rabbitmq", rabbitMq.GetConnectionString());

        var factory = new IdentityApiFactory(postgres, rabbitMq);
        await factory.InitializeDatabaseAsync();

        return factory;
    }

    public async Task ResetDatabaseAsync()
    {
        if (_respawner is null)
            throw new InvalidOperationException("Respawner has not been initialized.");

        await using var connection = new NpgsqlConnection(_postgres.GetConnectionString());
        await connection.OpenAsync();
        await _respawner.ResetAsync(connection);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:identity-db"] = _postgres.GetConnectionString(),
                ["ConnectionStrings:rabbitmq"] = _rabbitMq.GetConnectionString()
            });
        });

        builder.ConfigureServices(services =>
        {
            var dbContextDescriptors = services
                .Where(descriptor =>
                    descriptor.ServiceType == typeof(IdentityAppDbContext) ||
                    (descriptor.ServiceType.FullName?.Contains(nameof(IdentityAppDbContext), StringComparison.Ordinal) ?? false) ||
                    (descriptor.ServiceType.FullName?.Contains("DbContextOptions", StringComparison.Ordinal) ?? false) ||
                    (descriptor.ServiceType.FullName?.Contains("DbContextPool", StringComparison.Ordinal) ?? false) ||
                    (descriptor.ServiceType.FullName?.Contains("ScopedDbContextLease", StringComparison.Ordinal) ?? false))
                .ToList();

            foreach (var descriptor in dbContextDescriptors)
            {
                services.Remove(descriptor);
            }

            services.AddDbContextPool<IdentityAppDbContext>(options =>
            {
                options.UseNpgsql(_postgres.GetConnectionString(), npgsql =>
                {
                    npgsql.MigrationsHistoryTable("__EFMigrationsHistory_Identity", "identity");
                });
            });
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (disposing)
        {
            _rabbitMq.DisposeAsync().AsTask().GetAwaiter().GetResult();
            _postgres.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }

    private static PostgreSqlContainer CreatePostgresContainer()
        => new PostgreSqlBuilder()
            .WithImage("postgres:17-alpine")
            .WithDatabase("identity-db")
            .WithUsername("postgres")
            .WithPassword("postgres")
            .Build();

    private static RabbitMqContainer CreateRabbitMqContainer()
        => new RabbitMqBuilder()
            .WithImage("rabbitmq:4-management")
            .WithUsername("guest")
            .WithPassword("guest")
            .Build();

    private async Task InitializeDatabaseAsync()
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityAppDbContext>();
        await db.Database.MigrateAsync();

        await using var connection = new NpgsqlConnection(_postgres.GetConnectionString());
        await connection.OpenAsync();

        _respawner = await Respawner.CreateAsync(connection, new RespawnerOptions
        {
            DbAdapter = DbAdapter.Postgres,
            SchemasToInclude = ["identity"],
            TablesToIgnore =
            [
                new Table("identity", "__EFMigrationsHistory_Identity")
            ]
        });
    }
}
