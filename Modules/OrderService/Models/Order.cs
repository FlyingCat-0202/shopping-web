// Modules/OrderService/Models/Order.cs
namespace Shopping_web.Modules.OrderService.Models;
public class Order
{
    public Guid Id { get; set; } = Guid.NewGuid(); // Data primary key
    public required Guid CustomerId { get; set; } // Foreign key to the Customer
    public DateTime OrderDate { get; set; } = DateTime.UtcNow; // Date and time when the order was placed
    public string Status { get; set; } = "Pending"; // Order status (e.g., Pending, Processing, Shipped, Delivered, Cancelled)
    public string PaymentMethod { get; set; } = "COD"; // Payment method (e.g., COD, Credit Card, PayPal)
    public required decimal TotalAmount { get; set; } // Total amount for the order
    public IdentityService.Models.Customer Customer { get; set; } = null!; // Navigation property to the Customer
    public ICollection<OrderDetail> OrderDetails { get; set; } = []; // Navigation property to the order details (products in the order)
}