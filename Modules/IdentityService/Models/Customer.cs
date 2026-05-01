// Modules/Identity/Customer.cs
namespace Shopping_web.Modules.IdentityService.Models;
public class Customer
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string? Email { get; set; }
    public required string PasswordHash { get; set; }
    public string? FullName { get; set; }
    public string? PhoneNumber { get; set; }
    public string Role { get; set; } = "Customer";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}