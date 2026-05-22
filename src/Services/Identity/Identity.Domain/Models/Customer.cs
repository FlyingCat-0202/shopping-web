using Microsoft.AspNetCore.Identity;

namespace Identity.Domain.Models;
public class Customer : IdentityUser<Guid>
{
    public string? FullName { get; set; } // Optional full name of the customer
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow; // Timestamp when the customer account was created
    public string? Address { get; set; }
    public ICollection<RefreshToken> RefreshTokens { get; set; } = [];
}
