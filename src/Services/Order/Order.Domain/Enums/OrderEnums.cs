namespace Order.Domain.Enums;

public enum OrderStatus
{
    Pending,
    PaymentPending,
    Processing,
    Shipped,
    Delivered,
    ReturnRequested,
    Returned,
    ReturnRejected,
    Cancelled
}

public enum PaymentMethodType
{
    COD,
    CreditCard,
    PayPal
}

public enum PaymentStatus
{
    Unpaid,
    Paid,
    Refunded
}
