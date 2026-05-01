// Data/AppDbContext.cs
using Microsoft.EntityFrameworkCore;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    // --- IDENTITY ---
    public DbSet<Customer> Customers { get; set; }

    // --- PRODUCT ---
    public DbSet<Category> Categories { get; set; }
    public DbSet<Product> Products { get; set; }

    // --- ORDER ---
    public DbSet<CartItem> CartItems { get; set; }
    public DbSet<Order> Orders { get; set; }
    public DbSet<OrderDetail> OrderDetails { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // ==========================================
        // 1. IDENTITY SERVICE CONFIG
        // ==========================================
        modelBuilder.Entity<Customer>(entity =>
        {
            entity.HasIndex(c => c.Email).IsUnique(); // Email Unique
            entity.HasIndex(c => c.PhoneNumber).IsUnique(); // Phone Unique
        });

        // ==========================================
        // 2. PRODUCT SERVICE CONFIG
        // ==========================================
        modelBuilder.Entity<Product>(entity =>
        {
            entity.Property(p => p.Price).HasColumnType("decimal(18,2)");

            // Khóa ngoại nội bộ
            entity.HasOne(p => p.Category)
                  .WithMany(c => c.Products)
                  .HasForeignKey(p => p.CategoryId);
        });

        // ==========================================
        // 3. ORDER SERVICE CONFIG
        // ==========================================
        modelBuilder.Entity<CartItem>(entity =>
        {
            // Thiết lập Composite Primary Key
            entity.HasKey(c => new { c.CustomerId, c.ProductId });
        });

        modelBuilder.Entity<Order>(entity =>
        {
            entity.Property(o => o.TotalAmount).HasColumnType("decimal(18,2)");
        });

        modelBuilder.Entity<OrderDetail>(entity =>
        {
            // Thiết lập Composite Primary Key
            entity.HasKey(od => new { od.OrderId, od.ProductId });

            entity.Property(od => od.UnitPrice).HasColumnType("decimal(18,2)");

            // Khóa ngoại nội bộ
            entity.HasOne(od => od.Order)
                  .WithMany(o => o.OrderDetails)
                  .HasForeignKey(od => od.OrderId);
        });

    }
}