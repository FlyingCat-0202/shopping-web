// Modules/Product/Product.cs
public class Product
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; }
    public decimal Price { get; set; }
    public int StockQuantity { get; set; }
    public bool IsActive { get; set; } = true;

    // Khóa ngoại nội bộ về Category
    public int CategoryId { get; set; }
    public Category Category { get; set; }
}