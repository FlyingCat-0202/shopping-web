using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Shopping_web.Modules.OrderService.Models;
using Shopping_web.Modules.ProductService.Models;
using Shopping_web.Modules.IdentityService.Models;
public class AppDbContext(DbContextOptions<AppDbContext> options) : IdentityDbContext<Customer, IdentityRole<Guid>, Guid>(options) 
{
    // --- IDENTITY ---
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
            entity.ToTable("Customers"); // Đổi tên bảng mặc định từ AspNetUsers thành Customers
            entity.HasIndex(c => c.Email).IsUnique(); // Email Unique
            entity.HasIndex(c => c.PhoneNumber).IsUnique(); // Phone Unique

            entity.HasMany(c => c.CartItems) // One-to-Many relationship between Customer and CartItem
                .WithOne(ci => ci.Customer) // Each CartItem has one Customer
                .HasForeignKey(ci => ci.CustomerId) // Foreign key in CartItem pointing to Customer
                .OnDelete(DeleteBehavior.Cascade); // When a Customer is deleted, their CartItems are also deleted

            entity.HasMany(c => c.Orders) // One-to-Many relationship between Customer and Order
                .WithOne(o => o.Customer) // Each Order has one Customer
                .HasForeignKey(o => o.CustomerId) // Foreign key in Order pointing to Customer
                .OnDelete(DeleteBehavior.Restrict); // When a Customer is deleted, their Orders are not deleted to preserve order history
        });

        // ==========================================
        // 2. PRODUCT SERVICE CONFIG
        // ==========================================
        modelBuilder.Entity<Product>(entity =>
        {
            entity.Property(p => p.Price).HasColumnType("decimal(18,2)"); // Định dạng kiểu dữ liệu cho cột Price

            entity.HasOne(p => p.Category) // Each Product has one Category
                  .WithMany(c => c.Products) // Each Category has many Products
                  .HasForeignKey(p => p.CategoryId); // Foreign key in Product pointing to Category

            entity.HasMany(p => p.CartItems) // One-to-Many relationship between Product and CartItem
                .WithOne(ci => ci.Product) // Each CartItem has one Product
                .HasForeignKey(ci => ci.ProductId) // Foreign key in CartItem pointing to Product
                .OnDelete(DeleteBehavior.Cascade); // When a Product is deleted, related CartItems are also deleted

            entity.HasMany(p => p.OrderDetails) // One-to-Many relationship between Product and OrderDetail
                .WithOne(od => od.Product) // Each OrderDetail has one Product
                .HasForeignKey(od => od.ProductId) // Foreign key in OrderDetail pointing to Product
                .OnDelete(DeleteBehavior.Restrict); // When a Product is deleted, related OrderDetails are not deleted to preserve order history
        });

        // ==========================================
        // 3. ORDER SERVICE CONFIG
        // ==========================================
        modelBuilder.Entity<CartItem>(entity =>
        {
            entity.HasKey(c => new { c.CustomerId, c.ProductId }); // Thiết lập Composite Primary Key
        });

        modelBuilder.Entity<Order>(entity =>
        {
            entity.Property(o => o.TotalAmount).HasColumnType("decimal(18,2)"); // Định dạng kiểu dữ liệu cho cột TotalAmount
        });

        modelBuilder.Entity<OrderDetail>(entity =>
        {
            entity.HasKey(od => new { od.OrderId, od.ProductId }); // Thiết lập Composite Primary Key

            entity.Property(od => od.UnitPrice).HasColumnType("decimal(18,2)"); // Định dạng kiểu dữ liệu cho cột UnitPrice

            // Khóa ngoại nội bộ
            entity.HasOne(od => od.Order) // Each OrderDetail has one Order
                  .WithMany(o => o.OrderDetails) // Each Order has many OrderDetails
                  .HasForeignKey(od => od.OrderId); // Foreign key in OrderDetail pointing to Order

            entity.HasOne(od => od.Product) // Each OrderDetail has one Product
                  .WithMany(p => p.OrderDetails) // Each Product has many OrderDetails
                  .HasForeignKey(od => od.ProductId) // Foreign key in OrderDetail pointing to Product
                  .OnDelete(DeleteBehavior.Restrict); // When a Product is deleted, related OrderDetails are not deleted to preserve order history
        });
    }
}