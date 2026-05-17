using MassTransit;
using Microsoft.EntityFrameworkCore;
using Payment.Domain.Entities;

namespace Payment.Infrastructure.Data;

public class PaymentDbContext(DbContextOptions<PaymentDbContext> options) : DbContext(options)
{
    public DbSet<PaymentTransaction> Payments { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("payment");

        modelBuilder.AddInboxStateEntity();
        modelBuilder.AddOutboxMessageEntity();
        modelBuilder.AddOutboxStateEntity();

        modelBuilder.Entity<PaymentTransaction>(entity =>
        {
            entity.ToTable("Payments", "payment");
            entity.HasKey(p => p.Id);

            entity.Property(p => p.Amount).HasColumnType("decimal(18,2)");
            entity.Property(p => p.PaymentMethod).HasMaxLength(50);
            entity.Property(p => p.Status).HasConversion<string>().HasMaxLength(30);
            entity.Property(p => p.ProviderTransactionId).HasMaxLength(100);
            entity.Property(p => p.FailureReason).HasMaxLength(500);

            entity.ToTable(t => t.HasCheckConstraint("CK_Payments_Amount", "\"Amount\" > 0"));

            entity.HasIndex(p => p.OrderId)
                  .IsUnique()
                  .HasDatabaseName("IX_Payments_OrderId");

            entity.HasIndex(p => new { p.CustomerId, p.CreatedAt })
                  .HasDatabaseName("IX_Payments_CustomerId_CreatedAt");

            entity.HasIndex(p => new { p.Status, p.CreatedAt })
                  .HasDatabaseName("IX_Payments_Status_CreatedAt");
        });
    }
}
