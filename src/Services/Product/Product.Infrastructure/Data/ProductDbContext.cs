using MassTransit;
using Microsoft.EntityFrameworkCore;
using Product.Domain.Entities;
using ProductEntity = Product.Domain.Entities.Product;

namespace Product.Infrastructure.Data;

public class ProductDbContext(DbContextOptions<ProductDbContext> options) : DbContext(options)
{
    public DbSet<Category> Categories { get; set; } = null!;

    public DbSet<ProductEntity> Products { get; set; } = null!;

    public DbSet<IdempotentRequest> IdempotentRequests { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("product");

        modelBuilder.AddInboxStateEntity();
        modelBuilder.AddOutboxMessageEntity();
        modelBuilder.AddOutboxStateEntity();

        modelBuilder.Entity<Category>(entity =>
        {
            entity.ToTable("Categories", "product");
            entity.HasKey(c => c.Id);

            entity.HasIndex(c => c.Name)
                  .IsUnique()
                  .HasDatabaseName("IX_Categories_Name");
        });

        modelBuilder.Entity<ProductEntity>(entity =>
        {
            entity.ToTable("Products", "product");
            entity.HasKey(p => p.Id);

            entity.Property(p => p.Price)
                  .HasColumnType("decimal(18,2)");

            entity.Property(p => p.ImageUrl)
                  .HasMaxLength(1000);

            entity.Property(p => p.StockQuantity)
                  .IsConcurrencyToken();

            // Ràng buộc dữ liệu
            entity.ToTable(t => t.HasCheckConstraint("CK_Products_Price", "\"Price\" >= 0"));
            entity.ToTable(t => t.HasCheckConstraint("CK_Products_StockQuantity", "\"StockQuantity\" >= 0"));

            entity.HasIndex(p => p.Name)
                .HasDatabaseName("IX_Products_Name");

            // Quan hệ N-1 với Category
            entity.HasOne(p => p.Category)
                  .WithMany()
                  .HasForeignKey(p => p.CategoryId)
                  .OnDelete(DeleteBehavior.Restrict);

            entity.HasIndex(p => p.CategoryId)
                  .HasDatabaseName("IX_Products_CategoryId");
        });

        modelBuilder.Entity<IdempotentRequest>(entity =>
        {
            entity.ToTable("IdempotentRequests", "product");
            entity.HasKey(r => r.Id);
        });
    }
}
