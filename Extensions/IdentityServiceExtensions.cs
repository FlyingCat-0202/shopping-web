using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Shopping_web.Data;
using Shopping_web.Modules.IdentityService.Models;

namespace Shopping_web.Extensions;

public static class IdentityServiceExtensions
{
	public static IServiceCollection AddIdentityAuthentication(this IServiceCollection services, IConfiguration configuration)
	{
		services.AddDbContext<IdentityAppDbContext>(options =>
		{
			options.UseNpgsql(
				configuration.GetConnectionString("DefaultConnection"),
				npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory_Identity", "identity"));
		});

		services.AddIdentity<Customer, IdentityRole<Guid>>(options =>
		{
			options.Password.RequiredLength = 8;
			options.Password.RequireDigit = true;
			options.Password.RequireLowercase = true;
			options.Password.RequireUppercase = true;
			options.Password.RequireNonAlphanumeric = false;
		})
		.AddEntityFrameworkStores<IdentityAppDbContext>()
		.AddDefaultTokenProviders();

		services.AddAuthentication(options =>
		{
			options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
			options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
		})
		.AddJwtBearer(options =>
		{
			var jwtSection = configuration.GetSection("Jwt");
			var key = jwtSection["Key"];

			options.TokenValidationParameters = new TokenValidationParameters
			{
				ValidateIssuer = true,
				ValidateAudience = true,
				ValidateLifetime = true,
				ValidateIssuerSigningKey = true,
				ValidIssuer = jwtSection["Issuer"],
				ValidAudience = jwtSection["Audience"],
				IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key!)),
				ClockSkew = TimeSpan.Zero
			};
		});

		services.AddAuthorization();

		return services;
	}

}