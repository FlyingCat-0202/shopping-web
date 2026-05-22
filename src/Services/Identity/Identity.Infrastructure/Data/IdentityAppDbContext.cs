using Identity.Domain.Models;
using MassTransit;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Identity.Infrastructure.Data;

public class IdentityAppDbContext(DbContextOptions<IdentityAppDbContext> options)
    : IdentityDbContext<Customer, IdentityRole<Guid>, Guid>(options)
{
    public DbSet<RefreshToken> RefreshTokens { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("identity"); // Chia phòng cho Identity
        base.OnModelCreating(modelBuilder); // Gọi base để thiết lập các bảng mặc định của Identity

        // Tùy chỉnh bảng Users (Customer) để đặt tên và thêm các ràng buộc
        modelBuilder.Entity<Customer>(entity =>
        {
            entity.ToTable("Users", "identity"); // Đổi tên bảng thành "Users" trong schema "identity"
            entity.HasIndex(c => c.Email)
                  .IsUnique(); // email duy nhất
            entity.HasIndex(c => c.PhoneNumber)
                  .IsUnique(); // phone number duy nhất
        });

        modelBuilder.Entity<RefreshToken>(entity =>
        {
            entity.ToTable("RefreshTokens", "identity");
            entity.HasKey(t => t.Id);

            entity.Property(t => t.TokenHash)
                  .HasMaxLength(64);

            entity.Property(t => t.ReplacedByTokenHash)
                  .HasMaxLength(64);

            entity.Property(t => t.DeviceInfo)
                  .HasMaxLength(500);

            entity.HasIndex(t => t.TokenHash)
                  .IsUnique()
                  .HasDatabaseName("IX_RefreshTokens_TokenHash");

            entity.HasIndex(t => new { t.CustomerId, t.CreatedAt })
                  .HasDatabaseName("IX_RefreshTokens_CustomerId_CreatedAt");

            entity.HasIndex(t => new { t.CustomerId, t.RevokedAt })
                  .HasDatabaseName("IX_RefreshTokens_CustomerId_RevokedAt");

            entity.HasIndex(t => t.ExpiresAt)
                  .HasDatabaseName("IX_RefreshTokens_ExpiresAt");

            entity.HasOne(t => t.Customer)
                  .WithMany(c => c.RefreshTokens)
                  .HasForeignKey(t => t.CustomerId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.AddInboxStateEntity();
        modelBuilder.AddOutboxMessageEntity();
        modelBuilder.AddOutboxStateEntity();
    }
}
