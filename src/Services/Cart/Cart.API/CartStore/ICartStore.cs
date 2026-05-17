namespace Cart.API.CartStore;

public interface ICartStore
{
    Task<List<CartStoreItem>> GetItemsAsync(Guid customerId); 
    Task<bool> ItemExistsAsync(Guid customerId, Guid productId);
    Task<int> GetQuantityAsync(Guid customerId, Guid productId);
    Task UpsertItemAsync(Guid customerId, Guid productId, int quantity);
    Task<bool> RemoveItemAsync(Guid customerId, Guid productId);
    Task<int> RemoveItemsAsync(Guid customerId, IEnumerable<Guid> productIds);
    Task ClearAsync(Guid customerId);
}
