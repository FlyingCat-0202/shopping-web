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
            entity.HasIndex(c => c.Email)
                  .IsUnique(); // Email Unique
            entity.HasIndex(c => c.PhoneNumber)
                  .IsUnique(); // Phone Unique
        });

        // ==========================================
        // 2. PRODUCT SERVICE CONFIG
        // ==========================================
        modelBuilder.Entity<Product>(entity =>
        {
            entity.HasKey(p => p.Id);

            entity.Property(p => p.Price)
                  .HasColumnType("decimal(18,2)"); // Định dạng kiểu dữ liệu cho cột Price

            entity.HasOne(c => c.Category)
                  .WithMany()
                  .HasForeignKey(c => c.CategoryId)
                  .OnDelete(DeleteBehavior.Restrict);

            entity.HasIndex(p => p.CategoryId)
                  .HasDatabaseName("IX_Products_CategoryId");
        });

        modelBuilder.Entity<Category>(entity =>
        {
            entity.HasKey(c => c.Id);
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

            entity.Property(o => o.TotalAmount)
                  .HasColumnType("decimal(18,2)"); // Định dạng kiểu dữ liệu cho cột TotalAmount

            entity.HasIndex(o => o.CustomerId)
                  .HasDatabaseName("IX_Orders_CustomerId");
        });

        modelBuilder.Entity<OrderDetail>(entity =>
        {
            entity.HasKey(od => new { od.OrderId, od.ProductId }); // Thiết lập Composite Primary Key

            entity.Property(od => od.UnitPrice)
                  .HasColumnType("decimal(18,2)"); // Định dạng kiểu dữ liệu cho cột UnitPrice

            // Khóa ngoại nội bộ
            entity.HasOne(od => od.Order) // Each OrderDetail has one Order
                  .WithMany() // Each Order has many OrderDetails
                  .HasForeignKey(od => od.OrderId) // Foreign key in OrderDetail pointing to Order
                  .OnDelete(DeleteBehavior.Cascade);
        });
    }
}