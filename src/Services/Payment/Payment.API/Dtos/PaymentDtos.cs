namespace Payment.API.Dtos;

public record PaymentWebhookRequest(
    Guid PaymentId,
    bool Success,
    string? ProviderTransactionId,
    string? Reason);

public record PaymentMockWebhookRequest(
    bool Success,
    string? ProviderTransactionId,
    string? Reason);

public record PaymentPagedResult<T>(
    IReadOnlyList<T> Items,
    int TotalCount,
    int PageIndex,
    int PageSize);

public record PaymentResponse(
    Guid Id,
    Guid OrderId,
    decimal Amount,
    string PaymentMethod,
    string Status,
    string? ProviderTransactionId,
    string? FailureReason,
    DateTime CreatedAt,
    DateTime? CompletedAt);

public record AdminPaymentResponse(
    Guid Id,
    Guid OrderId,
    Guid CustomerId,
    decimal Amount,
    string PaymentMethod,
    string Status,
    string? ProviderTransactionId,
    string? FailureReason,
    DateTime CreatedAt,
    DateTime? CompletedAt);
