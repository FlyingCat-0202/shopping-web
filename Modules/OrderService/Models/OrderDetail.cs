// Modules/OrderService/Models/OrderDetail.cs
namespace Shopping_web.Modules.OrderService.Models;
public class OrderDetail
{
    public Guid OrderId { get; set; }
    public Order Order { get; set; } = null!;
    public Guid ProductId { get; set; } 
    public required string ProductName { get; set; }
    public required decimal UnitPrice { get; set; }
    public required int Quantity { get; set; }
}