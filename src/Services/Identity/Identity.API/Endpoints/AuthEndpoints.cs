using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography;
using System.Security.Claims;
using System.Text;
using Identity.API.Dtos;
using Identity.Domain.Models;
using EventBus.Contracts;
using EventBus.Extensions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using EventBus.Infrastructure;
using Identity.Infrastructure.Data;
using MassTransit;
using ServiceDefault;

namespace Identity.API.Endpoints;

public static class AuthEndpoints
{
	private const string CustomerRole = "Customer";
    private const int RefreshTokenLifetimeDays = 7;
    private const string CustomerProfileChangedRoutingKey = "customer-profile-changed";

    public static void MapIdentityEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth").WithTags("Auth");

	    group.MapPost("/register", async (
            RegisterRequest request,
            UserManager<Customer> userManager,
            RoleManager<IdentityRole<Guid>> roleManager,
            IdentityAppDbContext dbContext,
            IConfiguration configuration,
            IPublishEndpoint publishEndpoint,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
            {
	        var email = NormalizeEmail(request.Email);

            // ── Check for existing email or phone number ───────────────────────────────────────────────
            if (await userManager.FindByEmailAsync(email) is not null)
            {
                return Results.BadRequest(new { message = "Email already exists." });
            }

            if (await userManager.Users.AnyAsync(x => x.PhoneNumber == request.PhoneNumber))
            {
                return Results.BadRequest(new { message = "Phone number already exists." });
            }

	        if (!await roleManager.RoleExistsAsync(CustomerRole))
            {
	            await roleManager.CreateAsync(new IdentityRole<Guid>(CustomerRole));
            }

            var user = new Customer
            {
                UserName = email,
                Email = email,
                PhoneNumber = request.PhoneNumber,
                FullName = request.FullName
            };

            var result = await userManager.CreateAsync(user, request.Password);
            if (!result.Succeeded)
            {
                return Results.BadRequest(new
                {
                    message = "Registration failed.",
                    errors = result.Errors.Select(e => e.Description)
                });
            }

	        await userManager.AddToRoleAsync(user, CustomerRole);

            var roles = await userManager.GetRolesAsync(user);
            var token = CreateJwtToken(user, roles, configuration);
            var refreshToken = GenerateRefreshToken();

            dbContext.RefreshTokens.Add(CreateRefreshToken(user, refreshToken, httpContext));
            await PublishCustomerProfileChanged(publishEndpoint, user, cancellationToken);
            await dbContext.SaveChangesAsync(cancellationToken);

            return Results.Ok(BuildAuthResponse(user, token, refreshToken, roles));
        })
        .AddEndpointFilter<ValidationFilter<RegisterRequest>>();

        group.MapPost("/login", async (
            LoginRequest request,
            UserManager<Customer> userManager,
            IdentityAppDbContext dbContext,
            IConfiguration configuration,
            IPublishEndpoint publishEndpoint,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
	        Customer? user = await userManager.FindByEmailAsync(NormalizeEmail(request.EmailOrPhone));
            user ??= await userManager.Users.FirstOrDefaultAsync(x => x.PhoneNumber == request.EmailOrPhone);

            if (user is null)
            {
                return Results.BadRequest(new { message = "Wrong username or password" });
            }

            var passwordOk = await userManager.CheckPasswordAsync(user, request.Password);
            if (!passwordOk)
            {
                return Results.BadRequest(new { message = "Wrong username or password" });
            }

            var roles = await userManager.GetRolesAsync(user);
            var token = CreateJwtToken(user, roles, configuration);
            var refreshToken = GenerateRefreshToken();

            dbContext.RefreshTokens.Add(CreateRefreshToken(user, refreshToken, httpContext));
            await PublishCustomerProfileChanged(publishEndpoint, user, cancellationToken);
            await dbContext.SaveChangesAsync(cancellationToken);

            return Results.Ok(BuildAuthResponse(user, token, refreshToken, roles));
        })
        .AddEndpointFilter<ValidationFilter<LoginRequest>>();

        group.MapPost("/refresh", async (
            RefreshTokenRequest request,
            UserManager<Customer> userManager,
            IdentityAppDbContext dbContext,
            IConfiguration configuration,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(request.RefreshToken))
            {
                return Results.BadRequest(new { message = "Refresh token is required." });
            }

            var refreshTokenHash = HashRefreshToken(request.RefreshToken);
            var existingRefreshToken = await dbContext.RefreshTokens
                .Include(t => t.Customer)
                .FirstOrDefaultAsync(t => t.TokenHash == refreshTokenHash, cancellationToken);

            if (existingRefreshToken is null || !existingRefreshToken.IsActive)
            {
                return Results.BadRequest(new { message = "Invalid or expired refresh token." });
            }

            var user = existingRefreshToken.Customer;
            var roles = await userManager.GetRolesAsync(user);
            var newAccessToken = CreateJwtToken(user, roles, configuration);
            var newRefreshToken = GenerateRefreshToken();
            var newRefreshTokenHash = HashRefreshToken(newRefreshToken);

            existingRefreshToken.Revoke(newRefreshTokenHash);
            dbContext.RefreshTokens.Add(RefreshToken.Create(
                user.Id,
                newRefreshTokenHash,
                DateTime.UtcNow.AddDays(RefreshTokenLifetimeDays),
                GetDeviceInfo(httpContext)));

            await dbContext.SaveChangesAsync(cancellationToken);
            return Results.Ok(BuildAuthResponse(user, newAccessToken, newRefreshToken, roles));
        });

        group.MapPost("/logout", async (
            RefreshTokenRequest request,
            IdentityAppDbContext dbContext,
            CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(request.RefreshToken))
            {
                return Results.BadRequest(new { message = "Refresh token is required." });
            }

            var refreshTokenHash = HashRefreshToken(request.RefreshToken);
            var refreshToken = await dbContext.RefreshTokens
                .FirstOrDefaultAsync(t => t.TokenHash == refreshTokenHash, cancellationToken);

            if (refreshToken is not null)
            {
                refreshToken.Revoke();
                await dbContext.SaveChangesAsync(cancellationToken);
            }

            return Results.Ok(new { message = "Logged out successfully." });
        });

        group.MapGet("/sessions", async (
            ClaimsPrincipal principal,
            IdentityAppDbContext dbContext,
            CancellationToken cancellationToken) =>
        {
            if (!EndpointHelpers.TryGetCustomerId(principal, out var customerId))
            {
                return Results.Unauthorized();
            }

            var now = DateTime.UtcNow;
            var sessions = await dbContext.RefreshTokens
                .AsNoTracking()
                .Where(t => t.CustomerId == customerId)
                .OrderByDescending(t => t.CreatedAt)
                .Take(50)
                .Select(t => new RefreshTokenSessionResponse(
                    t.Id,
                    t.DeviceInfo,
                    t.CreatedAt,
                    t.ExpiresAt,
                    t.RevokedAt,
                    t.RevokedAt == null && t.ExpiresAt > now))
                .ToListAsync(cancellationToken);

            return Results.Ok(sessions);
        })
        .RequireAuthorization();

        group.MapDelete("/sessions/{sessionId:guid}", async (
            Guid sessionId,
            ClaimsPrincipal principal,
            IdentityAppDbContext dbContext,
            CancellationToken cancellationToken) =>
        {
            if (!EndpointHelpers.TryGetCustomerId(principal, out var customerId))
            {
                return Results.Unauthorized();
            }

            var refreshToken = await dbContext.RefreshTokens
                .FirstOrDefaultAsync(t => t.Id == sessionId && t.CustomerId == customerId, cancellationToken);

            if (refreshToken is null)
            {
                return Results.NotFound(new { message = "Session not found." });
            }

            refreshToken.Revoke();
            await dbContext.SaveChangesAsync(cancellationToken);

            return Results.Ok(new { message = "Session revoked successfully." });
        })
        .RequireAuthorization();

    }

	private static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();

	private static AuthResponse BuildAuthResponse(Customer user, string token, string refreshToken, IEnumerable<string> roles)
		=> new(token, refreshToken, user.FullName ?? string.Empty, user.Email ?? string.Empty, roles.FirstOrDefault() ?? CustomerRole);

    private static RefreshToken CreateRefreshToken(Customer user, string refreshToken, HttpContext httpContext)
        => RefreshToken.Create(
            user.Id,
            HashRefreshToken(refreshToken),
            DateTime.UtcNow.AddDays(RefreshTokenLifetimeDays),
            GetDeviceInfo(httpContext));

    private static Task PublishCustomerProfileChanged(
        IPublishEndpoint publishEndpoint,
        Customer user,
        CancellationToken cancellationToken)
        => publishEndpoint.Publish(new CustomerProfileChangedEvent
        {
            CustomerId = user.Id,
            Email = user.Email ?? string.Empty,
            PhoneNumber = user.PhoneNumber,
            FullName = user.FullName ?? string.Empty
        }, ctx => ctx.SetRoutingKey(CustomerProfileChangedRoutingKey), cancellationToken);

    private static string GenerateRefreshToken()
    {
        var randomNumber = new byte[64];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(randomNumber);
        return Convert.ToBase64String(randomNumber);
    }

    private static string HashRefreshToken(string refreshToken)
    {
        var tokenBytes = Encoding.UTF8.GetBytes(refreshToken);
        var hashBytes = SHA256.HashData(tokenBytes);

        return Convert.ToHexString(hashBytes);
    }

    private static string? GetDeviceInfo(HttpContext httpContext)
    {
        var userAgent = httpContext.Request.Headers["User-Agent"].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(userAgent))
        {
            return null;
        }

        return userAgent.Length <= 500 ? userAgent : userAgent[..500];
    }

    private static string CreateJwtToken(Customer user, IEnumerable<string> roles, IConfiguration configuration)
    {
        var jwtSection = configuration.GetSection("Jwt");
        var privateKey = jwtSection["PrivateKey"] ?? throw new InvalidOperationException("Missing Jwt:PrivateKey configuration.");
        var issuer = jwtSection["Issuer"] ?? throw new InvalidOperationException("Missing Jwt:Issuer configuration.");
        var audience = jwtSection["Audience"] ?? throw new InvalidOperationException("Missing Jwt:Audience configuration.");
        var minutes = int.TryParse(jwtSection["ExpiresMinutes"], out var value) ? value : 60;

        var claims = new List<Claim>
        { 
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.UserName ?? string.Empty),
            new(ClaimTypes.Email, user.Email ?? string.Empty),
            new("fullName", user.FullName ?? string.Empty)
        };

        claims.AddRange(roles.Select(role => new Claim(ClaimTypes.Role, role)));

        var credentials = new SigningCredentials(
            ServiceDefaultExtensions.CreateRsaSecurityKeyFromPem(privateKey),
            SecurityAlgorithms.RsaSha256);

        var token = new JwtSecurityToken(
            issuer: issuer,
            audience: audience,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(minutes),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
