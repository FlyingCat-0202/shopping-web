namespace Shopping_web.Modules.OrderService.Models;
public class CartItem
{
    public required Guid CustomerId { get; set; } // Foreign key to the Customer
    public required Guid ProductId { get; set; } // Foreign key to the Product
    public required int Quantity { get; set; } // Quantity of the product in the cart
}