// Modules/Product/Category.cs
public class Category
{
    public int Id { get; set; } 
    public required string Name { get; set; }
    public string? Description { get; set; }
    public ICollection<Product> Products { get; set; } = [];
}