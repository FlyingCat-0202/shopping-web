// Modules/OrderService/Models/OrderDetail.cs
namespace Shopping_web.Modules.OrderService.Models;
public class OrderDetail
{
    // Primary key is a composite key of OrderId and ProductId
    public Guid OrderId { get; set; } // Foreign key to the Order
    public Order Order { get; set; } = null!; // Navigation property to the Order
    public Guid ProductId { get; set; } // Foreign key to the Product
    public ProductService.Models.Product Product { get; set; } = null!; // Navigation property to the Product
    public required string ProductName { get; set; } // Store product name at the time of order
    public required decimal UnitPrice { get; set; } // Store product price at the time of order
    public required int Quantity { get; set; } // Quantity of the product in this order detail
}