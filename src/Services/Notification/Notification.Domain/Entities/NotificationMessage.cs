namespace Notification.Domain.Entities;

public class NotificationMessage
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid CustomerId { get; private set; }
    public Guid SourceEventId { get; private set; }
    public string DeduplicationKey { get; private set; } = string.Empty;
    public string Type { get; private set; } = string.Empty;
    public string Title { get; private set; } = string.Empty;
    public string Message { get; private set; } = string.Empty;
    public string? DataJson { get; private set; }
    public bool IsRead { get; private set; }
    public DateTime CreatedAt { get; private set; } = DateTime.UtcNow;
    public DateTime? ReadAt { get; private set; }

    private NotificationMessage()
    {
    }

    public static NotificationMessage Create(
        Guid customerId,
        Guid sourceEventId,
        string deduplicationKey,
        string type,
        string title,
        string message,
        string? dataJson,
        DateTime createdAt)
    {
        if (customerId == Guid.Empty)
            throw new InvalidOperationException("CustomerId không hợp lệ.");

        if (sourceEventId == Guid.Empty)
            throw new InvalidOperationException("SourceEventId không hợp lệ.");

        if (string.IsNullOrWhiteSpace(deduplicationKey))
            throw new InvalidOperationException("DeduplicationKey không được để trống.");

        if (string.IsNullOrWhiteSpace(title))
            throw new InvalidOperationException("Tiêu đề thông báo không được để trống.");

        if (string.IsNullOrWhiteSpace(message))
            throw new InvalidOperationException("Nội dung thông báo không được để trống.");

        return new NotificationMessage
        {
            CustomerId = customerId,
            SourceEventId = sourceEventId,
            DeduplicationKey = deduplicationKey.Trim(),
            Type = string.IsNullOrWhiteSpace(type) ? "General" : type.Trim(),
            Title = title.Trim(),
            Message = message.Trim(),
            DataJson = string.IsNullOrWhiteSpace(dataJson) ? null : dataJson,
            CreatedAt = createdAt == default ? DateTime.UtcNow : createdAt
        };
    }

    public void MarkRead(DateTime readAt)
    {
        if (IsRead)
            return;

        IsRead = true;
        ReadAt = readAt == default ? DateTime.UtcNow : readAt;
    }
}
