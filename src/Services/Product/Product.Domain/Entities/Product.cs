namespace Product.Domain.Entities;

public class Product
{
    private Product()
    {
    }

    public Guid Id { get; private set; } = Guid.NewGuid();
    public string Name { get; private set; } = string.Empty;
    public decimal Price { get; private set; }
    public int StockQuantity { get; private set; }
    public bool IsActive { get; private set; } = true;
    public string? Description { get; private set; }
    public string? ImageUrl { get; private set; }
    public int CategoryId { get; private set; }
    public Category Category { get; private set; } = null!;

    public static Product Create(
        string name,
        decimal price,
        int stockQuantity,
        int categoryId,
        string? description,
        string? imageUrl,
        bool isActive = true,
        Guid? id = null)
    {
        Validate(name, price, stockQuantity, categoryId);

        return new Product
        {
            Id = id ?? Guid.NewGuid(),
            Name = name.Trim(),
            Price = price,
            StockQuantity = stockQuantity,
            CategoryId = categoryId,
            Description = NormalizeOptionalText(description),
            ImageUrl = NormalizeOptionalText(imageUrl),
            IsActive = isActive
        };
    }

    public void Update(
        string name,
        decimal price,
        int stockQuantity,
        int categoryId,
        string? description,
        string? imageUrl,
        bool isActive)
    {
        Validate(name, price, stockQuantity, categoryId);

        Name = name.Trim();
        Price = price;
        StockQuantity = stockQuantity;
        CategoryId = categoryId;
        Description = NormalizeOptionalText(description);
        ImageUrl = NormalizeOptionalText(imageUrl);
        IsActive = isActive;
    }

    public void Deactivate()
        => IsActive = false;

    public void ReserveStock(int quantity)
    {
        if (quantity <= 0)
            throw new InvalidOperationException("Số lượng giữ kho phải lớn hơn 0.");

        if (quantity > StockQuantity)
            throw new InvalidOperationException($"Sản phẩm {Id} không đủ hàng.");

        StockQuantity -= quantity;
    }

    public void ReleaseStock(int quantity)
    {
        if (quantity <= 0)
            throw new InvalidOperationException("Số lượng hoàn kho phải lớn hơn 0.");

        StockQuantity += quantity;
    }

    private static void Validate(string name, decimal price, int stockQuantity, int categoryId)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new InvalidOperationException("Tên sản phẩm không được để trống.");

        if (price < 0)
            throw new InvalidOperationException("Giá sản phẩm không được âm.");

        if (stockQuantity < 0)
            throw new InvalidOperationException("Số lượng tồn kho không được âm.");

        if (categoryId <= 0)
            throw new InvalidOperationException("Danh mục không hợp lệ.");
    }

    private static string? NormalizeOptionalText(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
