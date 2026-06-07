using Identity.Domain.Models;
using Identity.Infrastructure.Data;
using MassTransit;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Product.Infrastructure.Data;

namespace DbSeeder;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        if (string.Equals(
                Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT"),
                "Production",
                StringComparison.OrdinalIgnoreCase))
        {
            Console.Error.WriteLine("DbSeeder is a development/test tool and refuses to run in Production.");
            return 2;
        }

        if (!TryParseArguments(args, out var count, out var seedType))
        {
            PrintUsage();
            return 1;
        }

        try
        {
            return seedType == SeedType.Products
                ? await SeedProductsAsync(count)
                : await SeedCustomersAsync(count);
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Seeding failed: {exception}");
            return 1;
        }
    }

    private static async Task<int> SeedProductsAsync(int count)
    {
        var productDbConnection = GetRequiredEnvironmentVariable("ConnectionStrings__product-db");
        var rabbitMqConnection = GetRequiredEnvironmentVariable("ConnectionStrings__rabbitmq");

        var services = CreateServiceCollection();
        services.AddDbContext<ProductDbContext>(options =>
            options.UseNpgsql(productDbConnection, npgsql =>
                npgsql.MigrationsHistoryTable("__EFMigrationsHistory_Product", "product")));
        services.AddMassTransit(configurator =>
            configurator.UsingRabbitMq((_, rabbit) => rabbit.Host(new Uri(rabbitMqConnection))));

        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<ProductDbContext>();
        await database.Database.MigrateAsync();

        var bus = scope.ServiceProvider.GetRequiredService<IBusControl>();
        await bus.StartAsync();
        try
        {
            await ProductGenerator.GenerateProductsAsync(database, bus, count);
        }
        finally
        {
            await bus.StopAsync();
        }

        return 0;
    }

    private static async Task<int> SeedCustomersAsync(int count)
    {
        var identityDbConnection = GetRequiredEnvironmentVariable("ConnectionStrings__identity-db");
        var customerPassword = GetRequiredEnvironmentVariable("SeedCustomers__Password");

        var services = CreateServiceCollection();
        services.AddDbContext<IdentityAppDbContext>(options =>
            options.UseNpgsql(identityDbConnection, npgsql =>
                npgsql.MigrationsHistoryTable("__EFMigrationsHistory_Identity", "identity")));
        services.AddIdentity<Customer, IdentityRole<Guid>>(options =>
            {
                options.Password.RequireDigit = false;
                options.Password.RequiredLength = 6;
                options.Password.RequireNonAlphanumeric = false;
                options.Password.RequireUppercase = false;
            })
            .AddEntityFrameworkStores<IdentityAppDbContext>()
            .AddDefaultTokenProviders();

        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<IdentityAppDbContext>();
        await database.Database.MigrateAsync();

        await CustomerGenerator.GenerateCustomersAsync(
            scope.ServiceProvider.GetRequiredService<UserManager<Customer>>(),
            scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>(),
            count,
            customerPassword);

        return 0;
    }

    private static ServiceCollection CreateServiceCollection()
    {
        var services = new ServiceCollection();
        services.AddLogging(logging => logging.AddConsole());
        return services;
    }

    private static string GetRequiredEnvironmentVariable(string name)
    {
        var value = Environment.GetEnvironmentVariable(name);
        return !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new InvalidOperationException($"Required environment variable '{name}' is missing.");
    }

    private static bool TryParseArguments(string[] args, out int count, out SeedType seedType)
    {
        count = 0;
        seedType = default;

        if (args.Length != 3 ||
            !args[0].Equals("seed", StringComparison.OrdinalIgnoreCase) ||
            !int.TryParse(args[1], out count) ||
            count <= 0)
        {
            return false;
        }

        seedType = args[2].ToLowerInvariant() switch
        {
            "product" or "products" => SeedType.Products,
            "customer" or "customers" => SeedType.Customers,
            _ => default
        };

        return seedType != default;
    }

    private static void PrintUsage()
    {
        Console.WriteLine(
            """
            Development/test database seeder

            Product seed requires:
              ConnectionStrings__product-db
              ConnectionStrings__rabbitmq

            Customer seed requires:
              ConnectionStrings__identity-db
              SeedCustomers__Password

            Usage:
              dotnet run --project src/Tools/DbSeeder -- seed 500 product
              dotnet run --project src/Tools/DbSeeder -- seed 100 customer
            """);
    }

    private enum SeedType
    {
        None,
        Products,
        Customers
    }
}
