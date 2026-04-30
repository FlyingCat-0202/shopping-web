// Modules/Order/CartItem.cs
public class CartItem
{
    public Guid CustomerId { get; set; } // Reference ID, không có Navigation Property
    public Guid ProductId { get; set; }  // Reference ID, không có Navigation Property
    public int Quantity { get; set; }
}

// Modules/Order/Order.cs
public class Order
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CustomerId { get; set; } // LƯU Ý: Tuyệt đối không khai báo "public Customer Customer {get;set;}" ở đây
    public DateTime OrderDate { get; set; } = DateTime.UtcNow;
    public string Status { get; set; }
    public string PaymentMethod { get; set; }
    public decimal TotalAmount { get; set; }

    // Navigation property nội bộ
    public ICollection<OrderDetail> OrderDetails { get; set; }
}

// Modules/Order/OrderDetail.cs
public class OrderDetail
{
    // Khóa ngoại nội bộ về Order
    public Guid OrderId { get; set; }
    public Order Order { get; set; }

    public Guid ProductId { get; set; } // Reference ID, không có Navigation property

    // Dữ liệu Snapshot (Chụp nhanh lúc đặt hàng)
    public string ProductName { get; set; }
    public decimal UnitPrice { get; set; }
    public int Quantity { get; set; }
}