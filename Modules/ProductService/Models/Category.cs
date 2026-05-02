// Modules/ProductService/Models/Category.cs
namespace Shopping_web.Modules.ProductService.Models;
public class Category
{
    public int Id { get; set; } // Data primary key
    public required string Name { get; set; } // Category name
    public string? Description { get; set; } // Optional category description
    public ICollection<Product> Products { get; set; } = []; // Navigation property to the products in this category
}