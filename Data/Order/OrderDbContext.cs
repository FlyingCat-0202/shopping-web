using Microsoft.EntityFrameworkCore;
using Shopping_web.Modules.OrderService.Models;

namespace Shopping_web.Data;

public class OrderDbContext(DbContextOptions<OrderDbContext> options) : DbContext(options)
{
    public DbSet<CartItem> CartItems { get; set; }
    public DbSet<Order> Orders { get; set; }
    public DbSet<OrderDetail> OrderDetails { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("order"); // Chia phòng cho Order

        // Cấu hình bảng CartItems
        modelBuilder.Entity<CartItem>(entity =>
        {
            entity.ToTable("CartItems", "order"); // Đặt tên bảng và schema
            entity.HasKey(c => new { c.CustomerId, c.ProductId }); // Khóa chính gồm CustomerId và ProductId

            entity.ToTable(t => t.HasCheckConstraint("CK_CartItems_Quantity", "\"Quantity\" > 0")); // Ràng buộc số lượng phải lớn hơn 0

            entity.HasIndex(c => c.ProductId) // Tạo index cho ProductId
                  .HasDatabaseName("IX_CartItems_ProductId"); 
        });

        // Cấu hình bảng Orders
        modelBuilder.Entity<Order>(entity =>
        {
            entity.ToTable("Orders", "order"); // Đặt tên bảng và schema

            entity.Property(o => o.TotalAmount) // Định nghĩa kiểu dữ liệu cho TotalAmount
                  .HasColumnType("decimal(18,2)");

            entity.ToTable(t => t.HasCheckConstraint("CK_Orders_TotalAmount", "\"TotalAmount\" >= 0")); // Ràng buộc tổng tiền phải lớn hơn hoặc bằng 0

            entity.HasIndex(o => o.CustomerId) // Tạo index cho CustomerId
                  .HasDatabaseName("IX_Orders_CustomerId");
        });

        // Cấu hình bảng OrderDetails
        modelBuilder.Entity<OrderDetail>(entity =>
        {
            entity.ToTable("OrderDetails", "order"); // Đặt tên bảng và schema
            entity.HasKey(od => new { od.OrderId, od.ProductId }); // Khóa chính gồm OrderId và ProductId

            entity.Property(od => od.UnitPrice) // Định nghĩa kiểu dữ liệu cho UnitPrice
                  .HasColumnType("decimal(18,2)");

            entity.ToTable(t => t.HasCheckConstraint("CK_OrderDetails_UnitPrice", "\"UnitPrice\" >= 0")); // Ràng buộc giá đơn vị phải lớn hơn hoặc bằng 0
            entity.ToTable(t => t.HasCheckConstraint("CK_OrderDetails_Quantity", "\"Quantity\" > 0")); // Ràng buộc số lượng phải lớn hơn 0

            entity.HasIndex(od => od.ProductId)
                  .HasDatabaseName("IX_OrderDetails_ProductId"); // Tạo index cho ProductId

            entity.HasOne(od => od.Order) // Thiết lập quan hệ 1-n với bảng Orders
                  .WithMany()
                  .HasForeignKey(od => od.OrderId)
                  .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
