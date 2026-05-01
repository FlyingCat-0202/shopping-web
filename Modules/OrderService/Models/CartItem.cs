namespace Shopping_web.Modules.OrderService.Models;
public class CartItem
{
    public required Guid CustomerId { get; set; } 
    public required Guid ProductId { get; set; }  
    public required int Quantity { get; set; }
}