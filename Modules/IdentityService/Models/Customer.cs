using Microsoft.AspNetCore.Identity;

namespace Shopping_web.Modules.IdentityService.Models;
public class Customer : IdentityUser<Guid>
{
    public string? FullName { get; set; } // Optional full name of the customer
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow; // Timestamp when the customer account was created
    public ICollection<OrderService.Models.CartItem> CartItems { get; set; } = []; // Navigation property to the cart items in the customer's cart
    public ICollection<OrderService.Models.Order> Orders { get; set; } = []; // Navigation property to the orders placed by the customer
}