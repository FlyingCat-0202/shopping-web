namespace Order.Domain.Entities;

public record DeliveryInfo
{
    public string ReceiverName { get; init; } = string.Empty;
    public string PhoneNumber { get; init; } = string.Empty;
    public string ShippingAddress { get; init; } = string.Empty;

    private DeliveryInfo() { }

    public DeliveryInfo(string receiverName, string phoneNumber, string shippingAddress)
    {
        if (string.IsNullOrWhiteSpace(receiverName))
            throw new ArgumentException("Tên người nhận không được để trống.");
        if (string.IsNullOrWhiteSpace(phoneNumber))
            throw new ArgumentException("Số điện thoại không được để trống.");
        if (string.IsNullOrWhiteSpace(shippingAddress))
            throw new ArgumentException("Địa chỉ giao hàng không được để trống.");

        ReceiverName = receiverName;
        PhoneNumber = phoneNumber;
        ShippingAddress = shippingAddress;
    }
}
