using System.Threading.RateLimiting;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.OpenApi;
using Microsoft.IdentityModel.Tokens;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using Scalar.AspNetCore;

namespace ServiceDefault;

public static class ServiceDefaultExtensions
{
    private const string FrontendCorsPolicy = "FrontendCors";
    private const string HealthEndpointPath = "/health";

    public static WebApplicationBuilder AddApiServiceDefaults(this WebApplicationBuilder builder)
    {
        builder.ConfigureOpenTelemetry();
        builder.Services.AddProblemDetails();
        builder.Services.AddHealthChecks();
        builder.Services.AddHttpLogging();
        builder.Services.AddFrontendCors(builder.Configuration, builder.Environment);
        builder.Services.AddApiRateLimiting(builder.Configuration);
        builder.Services.AddScalarOpenApi(builder.Environment.ApplicationName);

        return builder;
    }

    private static WebApplicationBuilder ConfigureOpenTelemetry(this WebApplicationBuilder builder)
    {
        builder.Services.AddLogging(loggingBuilder => loggingBuilder.AddOpenTelemetry());
        builder.Services.Configure<OpenTelemetryLoggerOptions>(logging =>
        {
            logging.IncludeFormattedMessage = true;
            logging.IncludeScopes = true;
        });

        builder.Services.AddOpenTelemetry()
            .WithMetrics(metrics =>
            {
                metrics
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddRuntimeInstrumentation();
            })
            .WithTracing(tracing =>
            {
                tracing
                    .AddSource(builder.Environment.ApplicationName)
                    .AddSource("MassTransit")
                    .AddAspNetCoreInstrumentation(options =>
                    {
                        options.Filter = context =>
                            !context.Request.Path.StartsWithSegments(HealthEndpointPath);
                    })
                    .AddHttpClientInstrumentation();
            });

        builder.AddOpenTelemetryExporters();

        return builder;
    }

    private static WebApplicationBuilder AddOpenTelemetryExporters(this WebApplicationBuilder builder)
    {
        if (string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]))
            return builder;

        builder.Services.Configure<OpenTelemetryLoggerOptions>(logging => logging.AddOtlpExporter());
        builder.Services.ConfigureOpenTelemetryMeterProvider(metrics => metrics.AddOtlpExporter());
        builder.Services.ConfigureOpenTelemetryTracerProvider(tracing => tracing.AddOtlpExporter());

        return builder;
    }

    public static IServiceCollection AddJwtAuthentication(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options =>
        {
            var jwtSection = configuration.GetSection("Jwt");
            var publicKey = jwtSection["PublicKey"] ?? throw new InvalidOperationException("Jwt:PublicKey not configured");
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
                IssuerSigningKey = CreateRsaSecurityKeyFromPem(publicKey)
            };
        });

        return services;
    }

    public static RsaSecurityKey CreateRsaSecurityKeyFromPem(string pem)
    {
        var rsa = RSA.Create();
        rsa.ImportFromPem(pem);
        return new RsaSecurityKey(rsa);
    }

    public static WebApplication UseApiServiceDefaults(this WebApplication app)
    {
        app.UseExceptionHandler();
        app.UseHttpLogging();
        app.UseApiSecurityHeaders();
        app.UseRateLimiter();
        app.UseFrontendCors();
        app.MapScalarOpenApi();

        return app;
    }

    public static IEndpointRouteBuilder MapApiHealthChecks(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapHealthChecks("/health");
        endpoints.MapHealthChecks("/health/live", new HealthCheckOptions
        {
            Predicate = _ => false
        });
        endpoints.MapHealthChecks("/health/ready", new HealthCheckOptions
        {
            ResultStatusCodes =
            {
                [HealthStatus.Healthy] = StatusCodes.Status200OK,
                [HealthStatus.Degraded] = StatusCodes.Status200OK,
                [HealthStatus.Unhealthy] = StatusCodes.Status503ServiceUnavailable
            }
        });

        return endpoints;
    }

    private static IServiceCollection AddApiRateLimiting(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var permitLimit = Math.Max(1, configuration.GetValue("RateLimiting:PermitLimit", 600));
        var windowSeconds = Math.Max(1, configuration.GetValue("RateLimiting:WindowSeconds", 60));
        var queueLimit = Math.Max(0, configuration.GetValue("RateLimiting:QueueLimit", 0));

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
            {
                var partitionKey = context.Request.Path.StartsWithSegments(HealthEndpointPath)
                    ? "health"
                    : context.User.Identity?.IsAuthenticated == true
                        ? context.User.Identity.Name ?? context.Connection.RemoteIpAddress?.ToString() ?? "authenticated"
                        : context.Connection.RemoteIpAddress?.ToString() ?? "anonymous";

                var partitionPermitLimit = partitionKey == "health"
                    ? Math.Max(permitLimit, 10_000)
                    : permitLimit;

                return RateLimitPartition.GetFixedWindowLimiter(partitionKey, _ => new FixedWindowRateLimiterOptions
                {
                    AutoReplenishment = true,
                    PermitLimit = partitionPermitLimit,
                    QueueLimit = queueLimit,
                    Window = TimeSpan.FromSeconds(windowSeconds)
                });
            });
        });

        return services;
    }

    private static WebApplication UseApiSecurityHeaders(this WebApplication app)
    {
        app.Use(async (context, next) =>
        {
            var headers = context.Response.Headers;
            headers.TryAdd("X-Content-Type-Options", "nosniff");
            headers.TryAdd("X-Frame-Options", "DENY");
            headers.TryAdd("Referrer-Policy", "no-referrer");
            headers.TryAdd("Permissions-Policy", "accelerometer=(), camera=(), geolocation=(), gyroscope=(), magnetometer=(), microphone=(), payment=(), usb=()");

            await next();
        });

        return app;
    }

    private static IServiceCollection AddScalarOpenApi(
        this IServiceCollection services,
        string applicationName)
    {
        var title = ToApiTitle(applicationName);

        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen(c =>
        {
            c.SwaggerDoc("v1", new OpenApiInfo { Title = title, Version = "v1" });

            const string bearerSchemeId = "Bearer";
            c.AddSecurityDefinition(bearerSchemeId, new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT"
            });
            c.AddSecurityRequirement(doc => new OpenApiSecurityRequirement
            {
                [new OpenApiSecuritySchemeReference(bearerSchemeId, doc)] = []
            });
        });

        return services;
    }

    private static WebApplication MapScalarOpenApi(this WebApplication app)
    {
        if (!app.Environment.IsDevelopment())
            return app;

        var title = ToApiTitle(app.Environment.ApplicationName);

        app.MapSwagger("/openapi/{documentName}.json");
        app.MapScalarApiReference(options =>
        {
            options.WithTitle(title)
                .WithOpenApiRoutePattern("/openapi/{documentName}.json");
        });

        return app;
    }

    private static string ToApiTitle(string applicationName)
        => string.IsNullOrWhiteSpace(applicationName)
            ? "API"
            : applicationName.Replace(".", " ").Replace("_", " ");

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

                throw new InvalidOperationException(
                    "Cors:AllowedOrigins must be configured explicitly outside Development.");
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
