using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;

namespace ServiceDefault;

public static class ServiceDefaultExtensions
{
    private const string FrontendCorsPolicy = "FrontendCors";
    private const string DevelopmentJwtKey = "development-only-shopping-service-signing-key-please-use-user-secrets";

    public static WebApplicationBuilder AddApiServiceDefaults(this WebApplicationBuilder builder)
    {
        builder.Services.AddProblemDetails();
        builder.Services.AddHealthChecks();
        builder.Services.AddHttpLogging();
        builder.Services.AddFrontendCors(builder.Configuration, builder.Environment);

        return builder;
    }

    public static IServiceCollection AddJwtAuthentication(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options =>
        {
            var jwtSection = configuration.GetSection("Jwt");
            var key = jwtSection["Key"];

            if (string.IsNullOrWhiteSpace(key))
            {
                if (!environment.IsDevelopment())
                    throw new InvalidOperationException("Jwt:Key not configured");

                key = DevelopmentJwtKey;
            }

            var issuer = jwtSection["Issuer"] ?? throw new InvalidOperationException("Jwt:Issuer not configured");
            var audience = jwtSection["Audience"] ?? throw new InvalidOperationException("Jwt:Audience not configured");

            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                ValidIssuer = issuer,
                ValidAudience = audience,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key))
            };
        });

        return services;
    }

    public static WebApplication UseApiServiceDefaults(this WebApplication app)
    {
        app.UseExceptionHandler();
        app.UseHttpLogging();
        app.UseFrontendCors();

        return app;
    }

    public static IServiceCollection AddFrontendCors(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        var allowedOrigins = configuration
            .GetSection("Cors:AllowedOrigins")
            .Get<string[]>() ?? [];

        services.AddCors(options =>
        {
            options.AddPolicy(FrontendCorsPolicy, policy =>
            {
                if (allowedOrigins.Length > 0)
                {
                    policy
                        .WithOrigins(allowedOrigins)
                        .AllowAnyHeader()
                        .AllowAnyMethod();

                    return;
                }

                if (environment.IsDevelopment())
                {
                    policy
                        .AllowAnyOrigin()
                        .AllowAnyHeader()
                        .AllowAnyMethod();

                    return;
                }

                policy
                    .WithOrigins("https://example.com")
                    .AllowAnyHeader()
                    .AllowAnyMethod();
            });
        });

        return services;
    }

    public static WebApplication UseFrontendCors(this WebApplication app)
    {
        app.UseCors(FrontendCorsPolicy);
        return app;
    }
}
