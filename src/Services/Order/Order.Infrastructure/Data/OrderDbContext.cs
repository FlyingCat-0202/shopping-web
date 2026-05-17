using MassTransit;
using Microsoft.EntityFrameworkCore;
using Order.Domain.Entities;
using OrderEntity = Order.Domain.Entities.Order;

namespace Order.Infrastructure.Data;

public class OrderDbContext(DbContextOptions<OrderDbContext> options) : DbContext(options)
{
    public DbSet<OrderEntity> Orders { get; set; } = null!;
    public DbSet<OrderDetail> OrderDetails { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("order");

        modelBuilder.Entity<OrderEntity>(entity =>
        {
            entity.ToTable("Orders", "order");
            entity.Property(o => o.Status).HasConversion<string>();
            entity.Property(o => o.PaymentMethod).HasConversion<string>();
            entity.Property(o => o.TotalAmount).HasColumnType("decimal(18,2)");
            entity.ToTable(t => t.HasCheckConstraint("CK_Orders_TotalAmount", "\"TotalAmount\" >= 0"));
            entity.HasIndex(o => new { o.CustomerId, o.OrderDate })
                  .HasDatabaseName("IX_Orders_CustomerId_OrderDate");
            entity.HasIndex(o => new { o.Status, o.OrderDate })
                  .HasDatabaseName("IX_Orders_Status_OrderDate");
            entity.OwnsOne(o => o.DeliveryDetails, a =>
            {
                a.Property(p => p.ReceiverName).HasColumnName("ReceiverName");
                a.Property(p => p.PhoneNumber).HasColumnName("PhoneNumber");
                a.Property(p => p.ShippingAddress).HasColumnName("ShippingAddress");
            });
        });

        modelBuilder.Entity<OrderDetail>(entity =>
        {
            entity.ToTable("OrderDetails", "order");
            entity.HasKey(od => new { od.OrderId, od.ProductId });
            entity.Property(od => od.UnitPrice).HasColumnType("decimal(18,2)");
            entity.ToTable(t => t.HasCheckConstraint("CK_OrderDetails_UnitPrice", "\"UnitPrice\" >= 0"));
            entity.ToTable(t => t.HasCheckConstraint("CK_OrderDetails_Quantity", "\"Quantity\" > 0"));
            entity.HasIndex(od => od.ProductId).HasDatabaseName("IX_OrderDetails_ProductId");
            entity.HasOne(od => od.Order)
                  .WithMany(o => o.Items)
                  .HasForeignKey(od => od.OrderId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        // MassTransit Outbox
        modelBuilder.AddInboxStateEntity();
        modelBuilder.AddOutboxMessageEntity();
        modelBuilder.AddOutboxStateEntity();
    }
}
