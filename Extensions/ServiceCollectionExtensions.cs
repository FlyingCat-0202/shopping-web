using Microsoft.EntityFrameworkCore;
using Shopping_web.Data;

namespace Shopping_web.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddServiceDbContexts(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<ProductDbContext>(options =>
        {
            options.UseNpgsql(
                configuration.GetConnectionString("DefaultConnection"),
                npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory_Product", "product"));
        });

        services.AddDbContext<OrderDbContext>(options =>
        {
            options.UseNpgsql(
                configuration.GetConnectionString("DefaultConnection"),
                npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory_Order", "order"));
        });

        return services;
    }
}
