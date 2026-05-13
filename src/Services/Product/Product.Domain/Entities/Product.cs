// Modules/ProductService/Models/Product.cs
namespace Product.Domain.Entities;

public class Product
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Name { get; set; }
    public required decimal Price { get; set; }
    public required int StockQuantity { get; set; }
    public bool IsActive { get; set; } = true;
    public string? Description { get; set; }
    public int CategoryId { get; set; }
    public Category Category { get; set; } = null!;
}