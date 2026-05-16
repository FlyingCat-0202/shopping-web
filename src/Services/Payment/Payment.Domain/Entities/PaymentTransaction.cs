using Payment.Domain.Enums;

namespace Payment.Domain.Entities;

public class PaymentTransaction
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid OrderId { get; private set; }
    public Guid CustomerId { get; private set; }
    public decimal Amount { get; private set; }
    public string PaymentMethod { get; private set; } = string.Empty;
    public PaymentStatus Status { get; private set; } = PaymentStatus.Pending;
    public string? ProviderTransactionId { get; private set; }
    public string? FailureReason { get; private set; }
    public DateTime CreatedAt { get; private set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; private set; }

    private PaymentTransaction()
    {
    }

    public static PaymentTransaction Create(
        Guid orderId,
        Guid customerId,
        decimal amount,
        string paymentMethod)
    {
        if (amount <= 0)
            throw new InvalidOperationException("Số tiền thanh toán phải lớn hơn 0.");

        if (string.IsNullOrWhiteSpace(paymentMethod))
            throw new InvalidOperationException("Phương thức thanh toán không được để trống.");

        return new PaymentTransaction
        {
            OrderId = orderId,
            CustomerId = customerId,
            Amount = amount,
            PaymentMethod = paymentMethod.Trim(),
            Status = PaymentStatus.Pending
        };
    }

    public void MarkSucceeded(string providerTransactionId)
    {
        if (Status == PaymentStatus.Succeeded)
            return;

        if (Status != PaymentStatus.Pending)
            throw new InvalidOperationException($"Không thể xác nhận thanh toán ở trạng thái {Status}.");

        Status = PaymentStatus.Succeeded;
        ProviderTransactionId = providerTransactionId;
        FailureReason = null;
        CompletedAt = DateTime.UtcNow;
    }

    public void MarkFailed(string reason)
    {
        if (Status == PaymentStatus.Failed)
            return;

        if (Status != PaymentStatus.Pending)
            throw new InvalidOperationException($"Không thể đánh dấu thất bại thanh toán ở trạng thái {Status}.");

        Status = PaymentStatus.Failed;
        FailureReason = string.IsNullOrWhiteSpace(reason) ? "Thanh toán thất bại." : reason;
        CompletedAt = DateTime.UtcNow;
    }
}
