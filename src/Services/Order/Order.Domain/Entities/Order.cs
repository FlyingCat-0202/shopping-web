using System.ComponentModel.DataAnnotations.Schema;
using Order.Domain.Enums;

namespace Order.Domain.Entities;

public class Order
{
    // ── Properties ────────────────────────────────────────────────────────────
    public Guid Id { get; private set; }
    public Guid CustomerId { get; private set; }
    public DateTime OrderDate { get; private set; } = DateTime.UtcNow;
    public PaymentMethodType PaymentMethod { get; private set; } = PaymentMethodType.COD;
    public OrderStatus Status { get; private set; } = OrderStatus.Pending;
    public decimal TotalAmount { get; private set; } = 0;

    // ── Value Objects ─────────────────────────────────────────────────────────
    public DeliveryInfo DeliveryDetails { get; private set; } = null!;

    // ── Collections (Aggregate Root) ──────────────────────────────────────────
    private readonly List<OrderDetail> _items = [];
    public IReadOnlyCollection<OrderDetail> Items => _items.AsReadOnly();
    private readonly List<OrderTimelineEvent> _timeline = [];
    public IReadOnlyCollection<OrderTimelineEvent> Timeline => _timeline.AsReadOnly();

    // ── Factory ───────────────────────────────────────────────────────────────
    public static Order Create(Guid customerId, PaymentMethodType paymentMethod,
        string receiverName, string phoneNumber, string shippingAddress)
    {
        var order = new Order
        {
            Id = Guid.NewGuid(),
            CustomerId = customerId,
            PaymentMethod = paymentMethod,
            DeliveryDetails = new DeliveryInfo(receiverName, phoneNumber, shippingAddress),
            Status = OrderStatus.Pending,
            TotalAmount = 0
        };

        order.AddTimelineEvent(
            OrderStatus.Pending,
            "Order submitted",
            "Đơn hàng đã được tạo và đang chờ giữ kho.",
            "Order");

        return order;
    }

    // ── Domain Behaviors ──────────────────────────────────────────────────────
    public void AddOrderItem(Guid productId, decimal unitPrice, int quantity)
    {
        var existing = _items.SingleOrDefault(i => i.ProductId == productId);
        if (existing != null)
            existing.AddQuantity(quantity);
        else
            _items.Add(new OrderDetail(Id, productId, unitPrice, quantity));
    }

    // USE FOR TESTING PURPOSES ONLY
    public void MarkStockReserved(Dictionary<Guid, decimal> prices)
        => MarkStockReserved(prices.Select(x =>
            new OrderItemSnapshot(x.Key, $"Product {x.Key}", null, x.Value)));

    public void MarkStockReserved(IEnumerable<OrderItemSnapshot> productSnapshots)
    {
        if (Status != OrderStatus.Pending)
            throw new InvalidOperationException($"Không thể giữ kho cho đơn hàng ở trạng thái {Status}.");

        var snapshots = productSnapshots.ToDictionary(x => x.ProductId);
        decimal total = 0;
        foreach (var item in _items)
        {
            if (!snapshots.TryGetValue(item.ProductId, out var snapshot))
                throw new InvalidOperationException(
                    $"Danh sách sản phẩm đã giữ kho không chứa sản phẩm {item.ProductId} trong đơn hàng.");

            item.UpdateUnitPrice(snapshot.UnitPrice);
            item.UpdateProductSnapshot(snapshot.ProductName, snapshot.ProductImageUrl);
            total += item.UnitPrice * item.Quantity;
        }

        TotalAmount = total;
        Status = IsOnlinePayment() ? OrderStatus.PaymentPending : OrderStatus.Processing;
    }

    public void ConfirmPayment()
    {
        if (Status == OrderStatus.Processing)
            return;

        if (Status != OrderStatus.PaymentPending)
            throw new InvalidOperationException($"Không thể xác nhận thanh toán ở trạng thái {Status}.");

        Status = OrderStatus.Processing;
    }

    public void Cancel()
    {
        if (!CanCancel()) throw new InvalidOperationException($"Không thể hủy đơn hàng ở trạng thái {Status}.");
        Status = OrderStatus.Cancelled;
    }

    public void CancelDueToPaymentFailure()
    {
        if (Status != OrderStatus.PaymentPending)
            throw new InvalidOperationException($"Không thể hủy thanh toán thất bại ở trạng thái {Status}.");
        Status = OrderStatus.Cancelled;
    }

    public void CancelDueToStockFailure()
    {
        if (Status != OrderStatus.Pending)
            throw new InvalidOperationException($"Không thể hủy đơn hàng ở trạng thái {Status}.");
        Status = OrderStatus.Cancelled;
    }

    public void Ship()
    {
        if (!CanShip()) throw new InvalidOperationException($"Không thể giao vận khi đơn hàng đang ở trạng thái {Status}.");
        Status = OrderStatus.Shipped;
    }

    public void Deliver()
    {
        if (!CanDeliver()) throw new InvalidOperationException($"Không thể xác nhận giao hàng khi đơn hàng đang ở trạng thái {Status}.");
        Status = OrderStatus.Delivered;
    }

    public void RequestReturn()
    {
        if (!CanRequestReturn()) throw new InvalidOperationException($"Không thể yêu cầu trả hàng khi đơn hàng đang ở trạng thái {Status}.");
        Status = OrderStatus.ReturnRequested;
    }

    public void ApproveReturn()
    {
        if (!CanApproveReturn()) throw new InvalidOperationException($"Không thể duyệt trả hàng khi đơn hàng đang ở trạng thái {Status}.");
        Status = OrderStatus.Returned;
    }

    public void RejectReturn()
    {
        if (!CanRejectReturn()) throw new InvalidOperationException($"Không thể từ chối trả hàng khi đơn hàng đang ở trạng thái {Status}.");
        Status = OrderStatus.ReturnRejected;
    }

    // ── Query Helpers ─────────────────────────────────────────────────────────
    public bool CanCancel()
        => Status == OrderStatus.PaymentPending ||
           (Status == OrderStatus.Processing && PaymentMethod == PaymentMethodType.COD);
    public bool CanShip() => Status == OrderStatus.Processing;
    public bool CanDeliver() => Status == OrderStatus.Shipped;
    public bool CanRequestReturn() => Status == OrderStatus.Delivered;
    public bool CanApproveReturn() => Status == OrderStatus.ReturnRequested;
    public bool CanRejectReturn() => Status == OrderStatus.ReturnRequested;
    public bool CanTrack() => Status is OrderStatus.Processing or OrderStatus.Shipped
        or OrderStatus.Delivered or OrderStatus.ReturnRequested
        or OrderStatus.Returned or OrderStatus.ReturnRejected;
    public bool CanReturn() => Status == OrderStatus.Delivered;
    public bool IsOnlinePayment() => PaymentMethod != PaymentMethodType.COD;
    public OrderTimelineEvent AddTimelineEvent(
        OrderStatus status,
        string title,
        string description,
        string source)
    {
        var timelineEvent = new OrderTimelineEvent(
            Id,
            status.ToString(),
            title,
            description,
            source);

        _timeline.Add(timelineEvent);
        return timelineEvent;
    }

    // ── Computed Properties ───────────────────────────────────────────────────
    [NotMapped]
    public PaymentStatus PaymentState => ComputePaymentState(Status, PaymentMethod);

    public static PaymentStatus ComputePaymentState(OrderStatus status, PaymentMethodType paymentMethod)
        => paymentMethod == PaymentMethodType.COD
            ? status switch
            {
                OrderStatus.Delivered or OrderStatus.ReturnRequested or OrderStatus.ReturnRejected => PaymentStatus.Paid,
                OrderStatus.Returned => PaymentStatus.Refunded,
                _ => PaymentStatus.Unpaid
            }
            : status switch
            {
                OrderStatus.Pending or OrderStatus.PaymentPending or OrderStatus.Cancelled => PaymentStatus.Unpaid,
                OrderStatus.Returned => PaymentStatus.Refunded,
                _ => PaymentStatus.Paid
            };
}

public sealed record OrderItemSnapshot(
    Guid ProductId,
    string ProductName,
    string? ProductImageUrl,
    decimal UnitPrice);
