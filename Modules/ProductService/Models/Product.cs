// Modules/ProductService/Models/Product.cs
namespace Shopping_web.Modules.ProductService.Models;
public class Product
{
    public Guid Id { get; set; } = Guid.NewGuid(); // Data primary key
    public required string Name { get; set; } // Product name
    public required decimal Price { get; set; } // Product price
    public required int StockQuantity { get; set; } // Available stock quantity
    public bool IsActive { get; set; } = true; // Indicates if the product is active and available for purchase
    public int CategoryId { get; set; } // Foreign key to the Category
    public Category Category { get; set; } = null!; // Navigation property to the Category
    public ICollection<OrderService.Models.CartItem> CartItems { get; set; } = []; // Navigation property to CartItems
    public ICollection<OrderService.Models.OrderDetail> OrderDetails { get; set; } = []; // Navigation property to OrderDetails
}