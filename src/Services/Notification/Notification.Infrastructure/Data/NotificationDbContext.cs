using MassTransit;
using Microsoft.EntityFrameworkCore;
using Notification.Domain.Entities;

namespace Notification.Infrastructure.Data;

public class NotificationDbContext(DbContextOptions<NotificationDbContext> options) : DbContext(options)
{
    public DbSet<NotificationRecipient> Recipients { get; set; } = null!;
    public DbSet<NotificationMessage> Notifications { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("notification");

        modelBuilder.AddInboxStateEntity();
        modelBuilder.AddOutboxMessageEntity();
        modelBuilder.AddOutboxStateEntity();

        modelBuilder.Entity<NotificationRecipient>(entity =>
        {
            entity.ToTable("Recipients", "notification");
            entity.HasKey(r => r.CustomerId);

            entity.Property(r => r.Email).HasMaxLength(256);
            entity.Property(r => r.PhoneNumber).HasMaxLength(32);
            entity.Property(r => r.FullName).HasMaxLength(200);

            entity.HasIndex(r => r.Email)
                .HasDatabaseName("IX_Recipients_Email");
        });

        modelBuilder.Entity<NotificationMessage>(entity =>
        {
            entity.ToTable("Notifications", "notification");
            entity.HasKey(n => n.Id);

            entity.Property(n => n.DeduplicationKey).HasMaxLength(240);
            entity.Property(n => n.Type).HasMaxLength(80);
            entity.Property(n => n.Title).HasMaxLength(200);
            entity.Property(n => n.Message).HasMaxLength(1000);
            entity.Property(n => n.DataJson).HasColumnType("jsonb");

            entity.HasIndex(n => n.SourceEventId)
                .IsUnique()
                .HasDatabaseName("IX_Notifications_SourceEventId");

            entity.HasIndex(n => n.DeduplicationKey)
                .IsUnique()
                .HasDatabaseName("IX_Notifications_DeduplicationKey");

            entity.HasIndex(n => new { n.CustomerId, n.CreatedAt })
                .HasDatabaseName("IX_Notifications_CustomerId_CreatedAt");

            entity.HasIndex(n => new { n.CustomerId, n.IsRead, n.CreatedAt })
                .HasDatabaseName("IX_Notifications_CustomerId_IsRead_CreatedAt");
        });
    }
}
