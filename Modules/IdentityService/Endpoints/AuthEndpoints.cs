using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Shopping_web.Modules.IdentityService.Dtos;
using Shopping_web.Modules.IdentityService.Models;

namespace Shopping_web.Modules.IdentityService.Endpoints;

public static class AuthEndpoints
{
	private const string CustomerRole = "Customer";

    public static void MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth").WithTags("Auth");

	    group.MapPost("/register", async (
            RegisterRequest request,
            UserManager<Customer> userManager,
            RoleManager<IdentityRole<Guid>> roleManager,
            IConfiguration configuration) =>
            {
	        var email = NormalizeEmail(request.Email);

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

            return Results.Ok(BuildAuthResponse(user, token, roles));
        });

        group.MapPost("/login", async (
            LoginRequest request,
            UserManager<Customer> userManager,
            IConfiguration configuration) =>
        {
	        Customer? user = await userManager.FindByEmailAsync(NormalizeEmail(request.EmailOrPhone));
            user ??= await userManager.Users.FirstOrDefaultAsync(x => x.PhoneNumber == request.EmailOrPhone);

            if (user is null)
            {
                return Results.Unauthorized();
            }

            var passwordOk = await userManager.CheckPasswordAsync(user, request.Password);
            if (!passwordOk)
            {
                return Results.Unauthorized();
            }

            var roles = await userManager.GetRolesAsync(user);
            var token = CreateJwtToken(user, roles, configuration);

            return Results.Ok(BuildAuthResponse(user, token, roles));
        });

    }

	private static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();

	private static AuthResponse BuildAuthResponse(Customer user, string token, IEnumerable<string> roles)
		=> new(token, user.FullName ?? string.Empty, user.Email ?? string.Empty, roles.FirstOrDefault() ?? CustomerRole);

    private static string CreateJwtToken(Customer user, IEnumerable<string> roles, IConfiguration configuration)
    {
        var jwtSection = configuration.GetSection("Jwt");
        var key = jwtSection["Key"] ?? throw new InvalidOperationException("Missing Jwt:Key configuration.");
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
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)),
            SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: issuer,
            audience: audience,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(minutes),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}