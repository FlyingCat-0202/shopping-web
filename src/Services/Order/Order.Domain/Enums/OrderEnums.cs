namespace Order.Domain.Enums;

public enum OrderStatus
{
    Pending,
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
