using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace EventBus.Extensions;

public static class MigrationExtensions
{
    public static async Task MigrateDatabaseAsync<TDbContext>(this WebApplication app)
        where TDbContext : DbContext
    {
        if (!app.Environment.IsDevelopment())
            return;

        using var scope = app.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<TDbContext>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<TDbContext>>();

        try
        {
            logger.LogInformation("Applying database migrations for {DbContext}.", typeof(TDbContext).Name);
            await dbContext.Database.MigrateAsync();
            logger.LogInformation("Database migrations applied for {DbContext}.", typeof(TDbContext).Name);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to apply database migrations for {DbContext}.", typeof(TDbContext).Name);
            throw;
        }
    }
}
