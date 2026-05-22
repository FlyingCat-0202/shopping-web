namespace Identity.Domain.Models;

public class RefreshToken
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid CustomerId { get; private set; }
    public string TokenHash { get; private set; } = string.Empty;
    public DateTime CreatedAt { get; private set; } = DateTime.UtcNow;
    public DateTime ExpiresAt { get; private set; }
    public DateTime? RevokedAt { get; private set; }
    public string? ReplacedByTokenHash { get; private set; }
    public string? DeviceInfo { get; private set; }
    public Customer Customer { get; private set; } = null!;

    public bool IsActive => RevokedAt is null && ExpiresAt > DateTime.UtcNow;

    public static RefreshToken Create(
        Guid customerId,
        string tokenHash,
        DateTime expiresAt,
        string? deviceInfo)
        => new()
        {
            CustomerId = customerId,
            TokenHash = tokenHash,
            ExpiresAt = expiresAt,
            DeviceInfo = string.IsNullOrWhiteSpace(deviceInfo) ? null : deviceInfo.Trim()
        };

    public void Revoke(string? replacedByTokenHash = null)
    {
        if (RevokedAt is not null)
            return;

        RevokedAt = DateTime.UtcNow;
        ReplacedByTokenHash = replacedByTokenHash;
    }
}
