using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Identity.API.Dtos;
using Identity.Domain.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using EventBus.Infrastructure;
using ServiceDefault;

namespace Identity.API.Endpoints;

public static class AuthEndpoints
{
	private const string CustomerRole = "Customer";
    public static void MapIdentityEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth").WithTags("Auth");

	    group.MapPost("/register", async (
            RegisterRequest request,
            UserManager<Customer> userManager,
            RoleManager<IdentityRole<Guid>> roleManager,
            IConfiguration configuration) =>
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
            
            user.RefreshToken = refreshToken;
            user.RefreshTokenExpiryTime = DateTime.UtcNow.AddDays(7);
            await userManager.UpdateAsync(user);

            return Results.Ok(BuildAuthResponse(user, token, refreshToken, roles));
        })
        .AddEndpointFilter<ValidationFilter<RegisterRequest>>();

        group.MapPost("/login", async (
            LoginRequest request,
            UserManager<Customer> userManager,
            IConfiguration configuration) =>
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
            
            user.RefreshToken = refreshToken;
            user.RefreshTokenExpiryTime = DateTime.UtcNow.AddDays(7);
            await userManager.UpdateAsync(user);

            return Results.Ok(BuildAuthResponse(user, token, refreshToken, roles));
        })
        .AddEndpointFilter<ValidationFilter<LoginRequest>>();

        group.MapPost("/refresh", async (
            RefreshTokenRequest request,
            UserManager<Customer> userManager,
            IConfiguration configuration) =>
        {
            var user = await userManager.Users.FirstOrDefaultAsync(u => u.RefreshToken == request.RefreshToken);
            
            if (user is null || user.RefreshTokenExpiryTime <= DateTime.UtcNow)
            {
                return Results.BadRequest(new { message = "Invalid or expired refresh token." });
            }

            var roles = await userManager.GetRolesAsync(user);
            var newAccessToken = CreateJwtToken(user, roles, configuration);
            var newRefreshToken = GenerateRefreshToken();

            user.RefreshToken = newRefreshToken;
            user.RefreshTokenExpiryTime = DateTime.UtcNow.AddDays(7);
            await userManager.UpdateAsync(user);

            return Results.Ok(BuildAuthResponse(user, newAccessToken, newRefreshToken, roles));
        });

    }

	private static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();

	private static AuthResponse BuildAuthResponse(Customer user, string token, string refreshToken, IEnumerable<string> roles)
		=> new(token, refreshToken, user.FullName ?? string.Empty, user.Email ?? string.Empty, roles.FirstOrDefault() ?? CustomerRole);

    private static string GenerateRefreshToken()
    {
        var randomNumber = new byte[64];
        using var rng = System.Security.Cryptography.RandomNumberGenerator.Create();
        rng.GetBytes(randomNumber);
        return Convert.ToBase64String(randomNumber);
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
