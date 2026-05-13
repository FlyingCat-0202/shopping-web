using Cart.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Order.Infrastructure.Data;

namespace Cart.Infrastructure.Data;

public class CartDbContext(DbContextOptions<CartDbContext> options) : DbContext(options)
{
    public DbSet<CartItem> CartItems { get; set; } = null!;
    public DbSet<IdempotentRequest> IdempotentRequests { get; set; } = null!;
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("cart");

        modelBuilder.Entity<CartItem>(entity =>
        {
            entity.ToTable("CartItems", "cart");
            entity.HasKey(c => new { c.CustomerId, c.ProductId });
            entity.ToTable(t => t.HasCheckConstraint("CK_CartItems_Quantity", "\"Quantity\" > 0"));
            entity.HasIndex(c => c.ProductId).HasDatabaseName("IX_CartItems_ProductId");
        });

        modelBuilder.Entity<IdempotentRequest>(entity =>
        {
            entity.ToTable("IdempotentRequests", "cart");
            entity.HasKey(r => r.Id);
        });
    }
}
