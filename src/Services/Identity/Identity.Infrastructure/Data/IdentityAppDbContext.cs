using Identity.Domain.Models;
using MassTransit;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Identity.Infrastructure.Data;

public class IdentityAppDbContext(DbContextOptions<IdentityAppDbContext> options)
    : IdentityDbContext<Customer, IdentityRole<Guid>, Guid>(options)
{
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

        modelBuilder.AddInboxStateEntity();
        modelBuilder.AddOutboxMessageEntity();
        modelBuilder.AddOutboxStateEntity();
    }
}
