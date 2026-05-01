// Modules/ProductService/Models/Category.cs
namespace Shopping_web.Modules.ProductService.Models;
public class Category
{
    public int Id { get; set; } 
    public required string Name { get; set; }
    public string? Description { get; set; }
    public ICollection<Product> Products { get; set; } = [];
}