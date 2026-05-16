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
using Microsoft.OpenApi;
using ServiceDefault;

var builder = WebApplication.CreateBuilder(args);

// ── DbContext (PostgreSQL) ──────────────────────────────────────────────────
builder.Services.AddDbContext<IdentityAppDbContext>(options =>
{
    options.UseNpgsql(
        builder.Configuration.GetConnectionString("identity-db")
            ?? builder.Configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("Missing connection string 'identity-db'."),
        npgsql =>
        {
            npgsql.MigrationsHistoryTable("__EFMigrationsHistory_Identity", "identity");
            npgsql.EnableRetryOnFailure(5);
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

// ── Swagger / OpenAPI ─────────────────────────────────────────────────────────
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "Identity API", Version = "v1" });

    var bearerSchemeId = "Bearer";
    c.AddSecurityDefinition(bearerSchemeId, new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        Description = "Nhập JWT token để xác thực."
    });
    c.AddSecurityRequirement(doc => new OpenApiSecurityRequirement
    {
        [new OpenApiSecuritySchemeReference(bearerSchemeId, doc)] = []
    });
});

// ── JWT Configuration ────────────────────────────────────────────────────────
// Identity API chủ yếu phát hành token, nhưng vẫn cần auth để bảo vệ profile/user endpoints.
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
        cfg.Host(new Uri(builder.Configuration.GetConnectionString("rabbitmq") ?? "amqp://guest:guest@localhost:5672/"));

        cfg.ConfigureEndpoints(context);
    });
});

var app = builder.Build();

await app.MigrateDatabaseAsync<IdentityAppDbContext>();
await IdentitySeedData.SeedAdminAsync(app);

// ── Middleware ────────────────────────────────────────────────────────────────
app.UseApiServiceDefaults();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "Identity API v1"));
}

app.UseAuthentication();
app.UseAuthorization();

// ── Endpoints ─────────────────────────────────────────────────────────────────
app.MapHealthChecks("/health");

// Đăng ký các Endpoint Đăng nhập, Đăng ký, Logout từ Module Identity
app.MapIdentityEndpoints();

app.Run();
