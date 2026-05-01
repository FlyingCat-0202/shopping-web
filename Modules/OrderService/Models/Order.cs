// Modules/OrderService/Models/Order.cs
namespace Shopping_web.Modules.OrderService.Models;
public class Order
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required Guid CustomerId { get; set; } 
    public DateTime OrderDate { get; set; } = DateTime.UtcNow;
    public string Status { get; set; } = "Pending";
    public string PaymentMethod { get; set; } = "COD";
    public required decimal TotalAmount { get; set; }
    public ICollection<OrderDetail> OrderDetails { get; set; } = [];
}