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

    // ── Factory ───────────────────────────────────────────────────────────────
    public static Order Create(Guid customerId, PaymentMethodType paymentMethod,
        string receiverName, string phoneNumber, string shippingAddress) => new()
        {
            Id = Guid.NewGuid(),
            CustomerId = customerId,
            PaymentMethod = paymentMethod,
            DeliveryDetails = new DeliveryInfo(receiverName, phoneNumber, shippingAddress),
            Status = OrderStatus.Pending,
            TotalAmount = 0
        };

    // ── Domain Behaviors ──────────────────────────────────────────────────────
    public void AddOrderItem(Guid productId, decimal unitPrice, int quantity)
    {
        var existing = _items.SingleOrDefault(i => i.ProductId == productId);
        if (existing != null)
            existing.AddQuantity(quantity);
        else
            _items.Add(new OrderDetail(Id, productId, unitPrice, quantity));
    }

    public void MarkStockReserved(Dictionary<Guid, decimal> prices)
    {
        if (Status != OrderStatus.Pending)
            throw new InvalidOperationException($"Không thể giữ kho cho đơn hàng ở trạng thái {Status}.");

        decimal total = 0;
        foreach (var item in _items)
        {
            if (prices.TryGetValue(item.ProductId, out var price))
                item.UpdateUnitPrice(price);
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
    public bool CanCancel() => Status is OrderStatus.PaymentPending or OrderStatus.Processing;
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
