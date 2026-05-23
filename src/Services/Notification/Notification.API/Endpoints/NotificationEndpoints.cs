using System.Security.Claims;
using EventBus.Extensions;
using Microsoft.EntityFrameworkCore;
using Notification.API.Dtos;
using Notification.Infrastructure.Data;

namespace Notification.API.Endpoints;

public static class NotificationEndpoints
{
    public static void MapNotificationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/notifications")
            .WithTags("Notifications")
            .RequireAuthorization();

        group.MapGet("/", GetMyNotifications)
            .WithName("GetMyNotifications");

        group.MapGet("/unread-count", GetUnreadCount)
            .WithName("GetUnreadNotificationCount");

        group.MapPut("/{id:guid}/read", MarkRead)
            .WithName("MarkNotificationRead");

        group.MapPut("/read-all", MarkAllRead)
            .WithName("MarkAllNotificationsRead");

        group.MapGet("/admin", GetAdminNotifications)
            .RequireAuthorization(EndpointHelpers.AdminOnly)
            .WithName("GetAdminNotifications");
    }

    private static async Task<IResult> GetMyNotifications(
        ClaimsPrincipal user,
        NotificationDbContext db,
        CancellationToken cancellationToken,
        int pageIndex = 0,
        int pageSize = 20,
        bool unreadOnly = false)
    {
        if (!EndpointHelpers.TryGetCustomerId(user, out var customerId))
            return Results.Unauthorized();

        return await QueryNotifications(
            db,
            cancellationToken,
            pageIndex,
            pageSize,
            customerId,
            unreadOnly);
    }

    private static async Task<IResult> GetUnreadCount(
        ClaimsPrincipal user,
        NotificationDbContext db,
        CancellationToken cancellationToken)
    {
        if (!EndpointHelpers.TryGetCustomerId(user, out var customerId))
            return Results.Unauthorized();

        var count = await db.Notifications
            .AsNoTracking()
            .CountAsync(n => n.CustomerId == customerId && !n.IsRead, cancellationToken);

        return Results.Ok(new { unreadCount = count });
    }

    private static async Task<IResult> MarkRead(
        Guid id,
        ClaimsPrincipal user,
        NotificationDbContext db,
        CancellationToken cancellationToken)
    {
        if (!EndpointHelpers.TryGetCustomerId(user, out var customerId))
            return Results.Unauthorized();

        var notification = await db.Notifications
            .FirstOrDefaultAsync(n => n.Id == id && n.CustomerId == customerId, cancellationToken);

        if (notification is null)
            return Results.NotFound();

        notification.MarkRead(DateTime.UtcNow);
        await db.SaveChangesAsync(cancellationToken);

        return Results.Ok(ToResponse(notification));
    }

    private static async Task<IResult> MarkAllRead(
        ClaimsPrincipal user,
        NotificationDbContext db,
        CancellationToken cancellationToken)
    {
        if (!EndpointHelpers.TryGetCustomerId(user, out var customerId))
            return Results.Unauthorized();

        var now = DateTime.UtcNow;
        var markedCount = await db.Notifications
            .Where(n => n.CustomerId == customerId && !n.IsRead)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(n => n.IsRead, true)
                .SetProperty(n => n.ReadAt, now), cancellationToken);

        return Results.Ok(new { markedCount });
    }

    private static Task<IResult> GetAdminNotifications(
        NotificationDbContext db,
        CancellationToken cancellationToken,
        int pageIndex = 0,
        int pageSize = 50,
        Guid? customerId = null,
        bool unreadOnly = false)
        => QueryNotifications(
            db,
            cancellationToken,
            pageIndex,
            pageSize,
            customerId,
            unreadOnly);

    private static async Task<IResult> QueryNotifications(
        NotificationDbContext db,
        CancellationToken cancellationToken,
        int pageIndex,
        int pageSize,
        Guid? customerId,
        bool unreadOnly)
    {
        pageSize = Math.Clamp(pageSize, 1, 100);
        pageIndex = Math.Max(pageIndex, 0);

        var query = db.Notifications.AsNoTracking();

        if (customerId.HasValue)
            query = query.Where(n => n.CustomerId == customerId.Value);

        if (unreadOnly)
            query = query.Where(n => !n.IsRead);

        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(n => n.CreatedAt)
            .Skip(pageIndex * pageSize)
            .Take(pageSize)
            .Select(n => new NotificationResponse(
                n.Id,
                n.CustomerId,
                n.Type,
                n.Title,
                n.Message,
                n.DataJson,
                n.IsRead,
                n.CreatedAt,
                n.ReadAt))
            .ToListAsync(cancellationToken);

        return Results.Ok(new NotificationPagedResult<NotificationResponse>(
            items,
            totalCount,
            pageIndex,
            pageSize));
    }

    private static NotificationResponse ToResponse(Notification.Domain.Entities.NotificationMessage notification)
        => new(
            notification.Id,
            notification.CustomerId,
            notification.Type,
            notification.Title,
            notification.Message,
            notification.DataJson,
            notification.IsRead,
            notification.CreatedAt,
            notification.ReadAt);
}
