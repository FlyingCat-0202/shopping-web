// Modules/Order/CartItem.cs
public class CartItem
{
    public required Guid CustomerId { get; set; } 
    public required Guid ProductId { get; set; }  
    public required int Quantity { get; set; }
}

// Modules/Order/Order.cs
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

// Modules/Order/OrderDetail.cs
public class OrderDetail
{
    public Guid OrderId { get; set; }
    public Order Order { get; set; } = null!;
    public Guid ProductId { get; set; } 
    public required string ProductName { get; set; }
    public required decimal UnitPrice { get; set; }
    public required int Quantity { get; set; }
}