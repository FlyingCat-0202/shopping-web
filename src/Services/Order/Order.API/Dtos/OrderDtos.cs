namespace Order.API.Dtos;

public record CreateOrderRequest
{
    public string PaymentMethod { get; set; } = string.Empty;
    public string ReceiverName { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;
    public string ShippingAddress { get; set; } = string.Empty;
    public List<OrderItemDto> Items { get; set; } = [];
}

public record OrderItemDto
{
    public Guid ProductId { get; set; }
    public int Quantity { get; set; }
}

public record OrderSummaryResponse(Guid Id, DateTime OrderDate, decimal TotalAmount,
    string Status, string PaymentStatus, bool CanCancel, bool CanTrack, bool CanReturn);

public record AdminOrderSummaryResponse(Guid Id, Guid CustomerId, DateTime OrderDate, decimal TotalAmount,
    string Status, string PaymentStatus, bool CanCancel, bool CanTrack, bool CanReturn);

public record OrderDetailResponse(Guid Id, DateTime OrderDate, decimal TotalAmount,
    string ReceiverName, string PhoneNumber, string ShippingAddress,
    string PaymentMethod, string Status, string PaymentStatus,
    bool CanCancel, bool CanTrack, bool CanReturn,
    List<OrderItemResponse> Items,
    List<OrderTimelineItemResponse> Timeline);

public record AdminOrderDetailResponse(Guid Id, Guid CustomerId, DateTime OrderDate, decimal TotalAmount,
    string ReceiverName, string PhoneNumber, string ShippingAddress,
    string PaymentMethod, string Status, string PaymentStatus,
    bool CanCancel, bool CanTrack, bool CanReturn,
    List<OrderItemResponse> Items,
    List<OrderTimelineItemResponse> Timeline);

public record OrderItemResponse(Guid ProductId, string ProductName, string? ProductImageUrl, int Quantity, decimal UnitPrice);
public record OrderTimelineItemResponse(
    Guid Id,
    string Status,
    string Title,
    string Description,
    string Source,
    DateTime OccurredAt);
public record PagedResult<T>(List<T> Items, int TotalCount, int PageIndex, int PageSize);