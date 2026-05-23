namespace Notification.Domain.Entities;

public class NotificationRecipient
{
    public Guid CustomerId { get; private set; }
    public string Email { get; private set; } = string.Empty;
    public string? PhoneNumber { get; private set; }
    public string FullName { get; private set; } = string.Empty;
    public DateTime CreatedAt { get; private set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; private set; } = DateTime.UtcNow;

    private NotificationRecipient()
    {
    }

    public static NotificationRecipient Create(
        Guid customerId,
        string email,
        string? phoneNumber,
        string fullName,
        DateTime occurredAt)
    {
        if (customerId == Guid.Empty)
            throw new InvalidOperationException("CustomerId không hợp lệ.");

        var recipient = new NotificationRecipient
        {
            CustomerId = customerId,
            CreatedAt = NormalizeOccurredAt(occurredAt),
            UpdatedAt = NormalizeOccurredAt(occurredAt)
        };

        recipient.UpdateContact(email, phoneNumber, fullName, occurredAt);
        return recipient;
    }

    public bool UpdateContact(string email, string? phoneNumber, string fullName, DateTime occurredAt)
    {
        var eventTime = NormalizeOccurredAt(occurredAt);
        if (eventTime < UpdatedAt)
            return false;

        if (string.IsNullOrWhiteSpace(email))
            throw new InvalidOperationException("Email người nhận không được để trống.");

        Email = email.Trim().ToLowerInvariant();
        PhoneNumber = string.IsNullOrWhiteSpace(phoneNumber) ? null : phoneNumber.Trim();
        FullName = string.IsNullOrWhiteSpace(fullName) ? Email : fullName.Trim();
        UpdatedAt = eventTime;

        return true;
    }

    private static DateTime NormalizeOccurredAt(DateTime occurredAt)
        => occurredAt == default ? DateTime.UtcNow : occurredAt;
}
