// Modules/ProductService/Models/Category.cs
namespace Product.Domain.Entities;

public class Category
{
    public int Id { get; set; } // Data primary key
    public required string Name { get; set; } // Category name
    public string? Description { get; set; } // Optional category description
}