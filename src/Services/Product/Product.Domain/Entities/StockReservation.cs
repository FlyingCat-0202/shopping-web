namespace Product.Domain.Entities;

public class StockReservation
{
    private StockReservation()
    {
    }

    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid OrderId { get; private set; }
    public Guid ProductId { get; private set; }
    public string ProductName { get; private set; } = string.Empty;
    public string? ProductImageUrl { get; private set; }
    public int Quantity { get; private set; }
    public decimal UnitPrice { get; private set; }
    public StockReservationStatus Status { get; private set; } = StockReservationStatus.Reserved;
    public DateTime ReservedAt { get; private set; } = DateTime.UtcNow;
    public DateTime? ReleasedAt { get; private set; }
    public string? ReleaseReason { get; private set; }

    public Product Product { get; private set; } = null!;

    public static StockReservation Create(
        Guid orderId,
        Guid productId,
        string productName,
        string? productImageUrl,
        int quantity,
        decimal unitPrice)
    {
        if (orderId == Guid.Empty)
            throw new InvalidOperationException("OrderId không hợp lệ.");

        if (productId == Guid.Empty)
            throw new InvalidOperationException("ProductId không hợp lệ.");

        if (quantity <= 0)
            throw new InvalidOperationException("Số lượng giữ kho phải lớn hơn 0.");

        if (unitPrice < 0)
            throw new InvalidOperationException("Giá sản phẩm không được âm.");

        return new StockReservation
        {
            OrderId = orderId,
            ProductId = productId,
            ProductName = string.IsNullOrWhiteSpace(productName) ? $"Product {productId}" : productName.Trim(),
            ProductImageUrl = string.IsNullOrWhiteSpace(productImageUrl) ? null : productImageUrl.Trim(),
            Quantity = quantity,
            UnitPrice = unitPrice,
            Status = StockReservationStatus.Reserved,
            ReservedAt = DateTime.UtcNow
        };
    }

    public bool Release(string reason)
    {
        if (Status == StockReservationStatus.Released)
            return false;

        Status = StockReservationStatus.Released;
        ReleasedAt = DateTime.UtcNow;
        ReleaseReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();

        return true;
    }
}

public enum StockReservationStatus
{
    Reserved,
    Released
}
