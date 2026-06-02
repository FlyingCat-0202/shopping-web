namespace Notification.API.Dtos;

public record NotificationPagedResult<T>(
    IReadOnlyList<T> Items,
    int TotalCount,
    int PageIndex,
    int PageSize,
    int UnreadCount = 0);

public record NotificationResponse(
    Guid Id,
    Guid CustomerId,
    string Type,
    string Title,
    string Message,
    string? DataJson,
    bool IsRead,
    DateTime CreatedAt,
    DateTime? ReadAt);
