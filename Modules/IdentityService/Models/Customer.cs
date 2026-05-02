// Modules/Identity/Customer.cs
namespace Shopping_web.Modules.IdentityService.Models;
public class Customer
{
    public Guid Id { get; set; } = Guid.NewGuid(); // Data primary key
    public string? Email { get; set; } // Optional email, can be used for login or contact
    public required string PasswordHash { get; set; } // Store hashed password for security
    public string? FullName { get; set; } // Optional full name of the customer
    public string? PhoneNumber { get; set; } // Optional phone number
    public string Role { get; set; } = "Customer"; // Default role is "Customer", can be extended to include "Admin" or other roles in the future
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow; // Timestamp when the customer account was created
    public ICollection<OrderService.Models.CartItem> CartItems { get; set; } = []; // Navigation property to the cart items in the customer's cart
    public ICollection<OrderService.Models.Order> Orders { get; set; } = []; // Navigation property to the orders placed by the customer
}