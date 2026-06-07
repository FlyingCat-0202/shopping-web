using EventBus.Extensions;
using Identity.API.Endpoints;
using Identity.API.Seed;
using Identity.API.Validators;
using Identity.Domain.Models;
using Identity.Infrastructure.Data;
using FluentValidation;
using MassTransit;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using ServiceDefault;

var builder = WebApplication.CreateBuilder(args);

// ── DbContext (PostgreSQL) ──────────────────────────────────────────────────
builder.AddNpgsqlDbContext<IdentityAppDbContext>("identity-db", configureDbContextOptions: options =>
{
    options.UseNpgsql(npgsql =>
    {
        npgsql.MigrationsHistoryTable("__EFMigrationsHistory_Identity", "identity");
    });
});

// ── ASP.NET Core Identity ───────────────────────────────────────────────────
builder.Services.AddIdentity<Customer, IdentityRole<Guid>>(options =>
{
    options.Password.RequireDigit = false;
    options.Password.RequiredLength = 6;
    options.Password.RequireNonAlphanumeric = false;
    options.Password.RequireUppercase = false;
})
.AddEntityFrameworkStores<IdentityAppDbContext>()
.AddDefaultTokenProviders();

// ── Infrastructure ────────────────────────────────────────────────────────────
builder.AddApiServiceDefaults();
builder.Services.AddValidatorsFromAssemblyContaining<RegisterRequestValidator>();

// ── JWT Configuration ────────────────────────────────────────────────────────
// Identity API chủ yếu phát hành token, nhưng vẫn cần auth để bảo vệ profile/user endpoints.
builder.Configuration.GetRequiredConfigurationValue("Jwt:PrivateKey");
builder.Services.AddJwtAuthentication(builder.Configuration);
builder.Services.AddAuthorization();

// ── MassTransit + RabbitMQ (Outbox Pattern) ──────────────────────────────────
builder.Services.AddMassTransit(x =>
{
    x.SetKebabCaseEndpointNameFormatter();

    // Sử dụng Outbox để đảm bảo sự kiện UserCreated được gửi thành công sau khi DB lưu thành công
    x.AddEntityFrameworkOutbox<IdentityAppDbContext>(o =>
    {
        o.UsePostgres();
        o.UseBusOutbox();
    });

    x.UsingRabbitMq((context, cfg) =>
    {
        cfg.UseMessageRetry(r => r.Incremental(3, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2)));
        cfg.Host(builder.Configuration.GetRequiredConnectionStringUri("rabbitmq"));

        cfg.ConfigureEndpoints(context);
    });
});

var app = builder.Build();

await app.MigrateDatabaseAsync<IdentityAppDbContext>();
await IdentitySeedData.SeedAdminAsync(app);

// ── Middleware ────────────────────────────────────────────────────────────────
app.UseApiServiceDefaults();

app.UseAuthentication();
app.UseAuthorization();

// ── Endpoints ─────────────────────────────────────────────────────────────────
app.MapApiHealthChecks();

// Đăng ký các Endpoint Đăng nhập, Đăng ký, Logout từ Module Identity
app.MapIdentityEndpoints();

app.Run();

public partial class Program;
