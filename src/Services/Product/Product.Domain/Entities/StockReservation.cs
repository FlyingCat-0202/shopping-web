namespace Product.Domain.Entities;

public class StockReservation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrderId { get; set; }
    public Guid ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string? ProductImageUrl { get; set; }
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public StockReservationStatus Status { get; set; } = StockReservationStatus.Reserved;
    public DateTime ReservedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ReleasedAt { get; set; }
    public string? ReleaseReason { get; set; }

    public Product Product { get; set; } = null!;

    public static StockReservation Create(
        Guid orderId,
        Guid productId,
        string productName,
        string? productImageUrl,
        int quantity,
        decimal unitPrice)
        => new()
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

    public bool Release(string reason)
    {
        if (Status == StockReservationStatus.Released)
            return false;

        Status = StockReservationStatus.Released;
        ReleasedAt = DateTime.UtcNow;
        ReleaseReason = reason;

        return true;
    }
}

public enum StockReservationStatus
{
    Reserved,
    Released
}
