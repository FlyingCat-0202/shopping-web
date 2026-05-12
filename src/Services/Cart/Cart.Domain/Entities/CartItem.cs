namespace Cart.Domain.Entities;

public class CartItem
{
    public Guid CustomerId { get; private set; }
    public Guid ProductId { get; private set; }
    public int Quantity { get; private set; }

    private CartItem() { }

    public CartItem(Guid customerId, Guid productId, int quantity)
    {
        if (quantity <= 0)
            throw new ArgumentException("Số lượng phải lớn hơn 0.");

        CustomerId = customerId;
        ProductId = productId;
        Quantity = quantity;
    }

    public void UpdateQuantity(int quantity)
    {
        if (quantity <= 0)
            throw new ArgumentException("Số lượng phải lớn hơn 0.");

        Quantity = quantity;
    }
}
