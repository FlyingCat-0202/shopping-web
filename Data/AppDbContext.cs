// Data/AppDbContext.cs
using Microsoft.EntityFrameworkCore;
using Shopping_web.Modules.OrderService.Models;
using Shopping_web.Modules.ProductService.Models;
using Shopping_web.Modules.IdentityService.Models;
public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options) 
{
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

            entity.HasMany(c => c.CartItems)
                .WithOne(ci => ci.Customer)
                .HasForeignKey(ci => ci.CustomerId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasMany(c => c.Orders)
                .WithOne(o => o.Customer)
                .HasForeignKey(o => o.CustomerId)
                .OnDelete(DeleteBehavior.Restrict);
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

            entity.HasMany(p => p.CartItems)
                .WithOne(ci => ci.Product)
                .HasForeignKey(ci => ci.ProductId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasMany(p => p.OrderDetails)
                .WithOne(od => od.Product)
                .HasForeignKey(od => od.ProductId)
                .OnDelete(DeleteBehavior.Restrict);
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

            entity.HasOne(od => od.Product)
                  .WithMany(p => p.OrderDetails)
                  .HasForeignKey(od => od.ProductId)
                  .OnDelete(DeleteBehavior.Restrict);
        });
    }
}