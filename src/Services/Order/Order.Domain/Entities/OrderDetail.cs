namespace Order.Domain.Entities;

public class OrderDetail
{
    public Guid OrderId { get; private set; }
    public Order Order { get; private set; } = null!;
    public Guid ProductId { get; private set; }
    public decimal UnitPrice { get; private set; }
    public int Quantity { get; private set; }

    private OrderDetail() { }

    internal OrderDetail(Guid orderId, Guid productId, decimal unitPrice, int quantity)
    {
        if (quantity <= 0) throw new ArgumentException("Số lượng phải lớn hơn 0.");
        if (unitPrice < 0) throw new ArgumentException("Đơn giá không hợp lệ.");
        OrderId = orderId;
        ProductId = productId;
        UnitPrice = unitPrice;
        Quantity = quantity;
    }

    internal void AddQuantity(int quantity)
    {
        if (quantity <= 0) throw new ArgumentException("Số lượng thêm vào phải lớn hơn 0.");
        Quantity += quantity;
    }

    internal void UpdateUnitPrice(decimal unitPrice)
    {
        if (unitPrice < 0) throw new ArgumentException("Đơn giá không hợp lệ.");
        UnitPrice = unitPrice;
    }
}
