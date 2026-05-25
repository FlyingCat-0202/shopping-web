using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Product.Infrastructure.Data;
using Identity.Infrastructure.Data;
using Identity.Domain.Models;
using MassTransit;

namespace DbSeeder;

class Program
{
    static async Task<int> Main(string[] args)
    {
        Console.WriteLine("======================================");
        Console.WriteLine("       Shopping Web Database Seeder   ");
        Console.WriteLine("======================================");

        // Command parsing
        if (args.Length < 3 || !args[0].Equals("seed", StringComparison.OrdinalIgnoreCase))
        {
            PrintUsage();
            return 1;
        }

        string type = args[2].ToLowerInvariant();
        if (type != "product" && type != "products" && type != "customer" && type != "customers")
        {
            PrintUsage();
            return 1;
        }

        if (!int.TryParse(args[1], out int count) || count <= 0)
        {
            Console.WriteLine("Error: Count must be a valid positive integer.");
            PrintUsage();
            return 1;
        }

        var postgresUsername = Environment.GetEnvironmentVariable("Parameters__postgres-username") ?? "myuser";
        var postgresPassword = Environment.GetEnvironmentVariable("Parameters__postgres-password") ?? "pnthuc1609";
        var rabbitMqUsername = Environment.GetEnvironmentVariable("Parameters__rabbitmq-username") ?? "guest";
        var rabbitMqPassword = Environment.GetEnvironmentVariable("Parameters__rabbitmq-password") ?? "guest";

        var productDbConn = Environment.GetEnvironmentVariable("ConnectionStrings__product-db")
                            ?? $"Host=localhost;Port=5432;Database=product-db;Username={postgresUsername};Password={postgresPassword}";

        var identityDbConn = Environment.GetEnvironmentVariable("ConnectionStrings__identity-db")
                             ?? $"Host=localhost;Port=5432;Database=identity-db;Username={postgresUsername};Password={postgresPassword}";

        var rabbitMqConn = Environment.GetEnvironmentVariable("ConnectionStrings__rabbitmq")
                           ?? $"amqp://{rabbitMqUsername}:{rabbitMqPassword}@localhost:5672/";

        // Setup DI container
        var services = new ServiceCollection();

        services.AddLogging(configure => configure.AddConsole());

        services.AddDbContext<ProductDbContext>(options =>
            options.UseNpgsql(productDbConn, npgsql =>
            {
                npgsql.MigrationsHistoryTable("__EFMigrationsHistory_Product", "product");
            }));

        services.AddDbContext<IdentityAppDbContext>(options =>
            options.UseNpgsql(identityDbConn, npgsql =>
            {
                npgsql.MigrationsHistoryTable("__EFMigrationsHistory_Identity", "identity");
            }));

        services.AddIdentity<Customer, IdentityRole<Guid>>(options =>
        {
            options.Password.RequireDigit = false;
            options.Password.RequiredLength = 6;
            options.Password.RequireNonAlphanumeric = false;
            options.Password.RequireUppercase = false;
        })
        .AddEntityFrameworkStores<IdentityAppDbContext>()
        .AddDefaultTokenProviders();

        var serviceProvider = services.BuildServiceProvider();

        // Database migrations
        try
        {
            Console.WriteLine("Checking database migrations...");
            using var scope = serviceProvider.CreateScope();
            var pDb = scope.ServiceProvider.GetRequiredService<ProductDbContext>();
            await pDb.Database.MigrateAsync();
            var iDb = scope.ServiceProvider.GetRequiredService<IdentityAppDbContext>();
            await iDb.Database.MigrateAsync();
            Console.WriteLine("Databases are migrated.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Migration error: {ex.Message}");
            return 1;
        }

        // Execute seeding
        try
        {
            using var scope = serviceProvider.CreateScope();
            if (type.StartsWith("product"))
            {
                var dbContext = scope.ServiceProvider.GetRequiredService<ProductDbContext>();
                
                // Set up and start direct RabbitMQ bus to publish integration events
                Console.WriteLine("Connecting to RabbitMQ event bus...");
                var busControl = Bus.Factory.CreateUsingRabbitMq(cfg =>
                {
                    cfg.Host(new Uri(rabbitMqConn));
                });
                await busControl.StartAsync();

                try
                {
                    await ProductGenerator.GenerateProductsAsync(dbContext, busControl, count);
                }
                finally
                {
                    await busControl.StopAsync();
                }
            }
            else
            {
                var userManager = scope.ServiceProvider.GetRequiredService<UserManager<Customer>>();
                var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
                var dbContext = scope.ServiceProvider.GetRequiredService<IdentityAppDbContext>();

                await CustomerGenerator.GenerateCustomersAsync(userManager, roleManager, dbContext, count);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error during seeding process: {ex}");
            return 1;
        }

        Console.WriteLine("Seeding process completed successfully.");
        return 0;
    }

    private static void PrintUsage()
    {
        Console.WriteLine("\nUsage:");
        Console.WriteLine("  dotnet run --project src/Tools/DbSeeder -- seed [count] product");
        Console.WriteLine("  dotnet run --project src/Tools/DbSeeder -- seed [count] customer");
        Console.WriteLine("\nExample:");
        Console.WriteLine("  dotnet run --project src/Tools/DbSeeder -- seed 500 product");
        Console.WriteLine("  dotnet run --project src/Tools/DbSeeder -- seed 100 customer");
        Console.WriteLine("======================================\n");
    }
}
