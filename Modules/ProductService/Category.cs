// Modules/Product/Category.cs
public class Category
{
    public int Id { get; set; } // Tự động tăng
    public string Name { get; set; }
    public string Description { get; set; }

    // Navigation property nội bộ
    public ICollection<Product> Products { get; set; }
}