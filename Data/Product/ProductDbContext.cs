using Microsoft.EntityFrameworkCore;
using Shopping_web.Modules.ProductService.Models;

namespace Shopping_web.Data;

public class ProductDbContext(DbContextOptions<ProductDbContext> options) : DbContext(options)
{
    public DbSet<Category> Categories { get; set; }
    public DbSet<Product> Products { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("product");

        modelBuilder.Entity<Category>(entity =>
        {
            entity.ToTable("Categories", "product");
            entity.HasKey(c => c.Id);

            entity.HasIndex(c => c.Name)
                  .IsUnique()
                  .HasDatabaseName("IX_Categories_Name");
        });

        modelBuilder.Entity<Product>(entity =>
        {
            entity.ToTable("Products", "product");
            entity.HasKey(p => p.Id);

            entity.Property(p => p.Price)
                  .HasColumnType("decimal(18,2)");

            entity.ToTable(t => t.HasCheckConstraint("CK_Products_Price", "\"Price\" >= 0"));
            entity.ToTable(t => t.HasCheckConstraint("CK_Products_StockQuantity", "\"StockQuantity\" >= 0"));

            entity.HasIndex(p => p.Name)
                .HasDatabaseName("IX_Products_Name");

            entity.HasOne(p => p.Category)
                  .WithMany()
                  .HasForeignKey(p => p.CategoryId)
                  .OnDelete(DeleteBehavior.Restrict);

            entity.HasIndex(p => p.CategoryId)
                  .HasDatabaseName("IX_Products_CategoryId");
        });
    }
}
