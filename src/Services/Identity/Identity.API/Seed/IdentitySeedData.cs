using Identity.Domain.Models;
using Microsoft.AspNetCore.Identity;

namespace Identity.API.Seed;

public static class IdentitySeedData
{
    private const string AdminRole = "Admin";
    private const string CustomerRole = "Customer";

    public static async Task SeedAdminAsync(WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<Customer>>();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("IdentitySeedData");

        await EnsureRoleAsync(roleManager, AdminRole);
        await EnsureRoleAsync(roleManager, CustomerRole);

        var adminSection = app.Configuration.GetSection("SeedAdmin");
        var email = adminSection["Email"] ?? "admin@shopping.local";
        var password = adminSection["Password"] ?? "Admin123";
        var fullName = adminSection["FullName"] ?? "System Admin";
        var phoneNumber = adminSection["PhoneNumber"] ?? "0900000000";

        var normalizedEmail = email.Trim().ToLowerInvariant();
        var admin = await userManager.FindByEmailAsync(normalizedEmail);

        if (admin is null)
        {
            admin = new Customer
            {
                UserName = normalizedEmail,
                Email = normalizedEmail,
                FullName = fullName,
                PhoneNumber = phoneNumber
            };

            var createResult = await userManager.CreateAsync(admin, password);
            if (!createResult.Succeeded)
            {
                var errors = string.Join("; ", createResult.Errors.Select(error => error.Description));
                throw new InvalidOperationException($"Failed to seed admin user: {errors}");
            }

            logger.LogInformation("Seeded admin user {Email}.", normalizedEmail);
        }

        if (!await userManager.IsInRoleAsync(admin, AdminRole))
        {
            var roleResult = await userManager.AddToRoleAsync(admin, AdminRole);
            if (!roleResult.Succeeded)
            {
                var errors = string.Join("; ", roleResult.Errors.Select(error => error.Description));
                throw new InvalidOperationException($"Failed to assign Admin role: {errors}");
            }
        }
    }

    private static async Task EnsureRoleAsync(RoleManager<IdentityRole<Guid>> roleManager, string role)
    {
        if (!await roleManager.RoleExistsAsync(role))
            await roleManager.CreateAsync(new IdentityRole<Guid>(role));
    }
}
