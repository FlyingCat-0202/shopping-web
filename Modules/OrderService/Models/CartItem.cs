namespace Shopping_web.Modules.OrderService.Models;
public class CartItem
{
    public required Guid CustomerId { get; set; } // Foreign key to the Customer
    public required Guid ProductId { get; set; } // Foreign key to the Product
    public required int Quantity { get; set; } // Quantity of the product in the cart
    public IdentityService.Models.Customer Customer { get; set; } = null!; // Navigation property to the Customer
    public ProductService.Models.Product Product { get; set; } = null!; // Navigation property to the Product
}