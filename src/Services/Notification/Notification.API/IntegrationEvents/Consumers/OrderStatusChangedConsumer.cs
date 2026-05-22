using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using EventBus.Contracts;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Notification.Domain.Entities;
using Notification.Infrastructure.Data;

namespace Notification.API.IntegrationEvents.Consumers;

public class OrderStatusChangedConsumer(
    NotificationDbContext dbContext,
    ILogger<OrderStatusChangedConsumer> logger)
    : IConsumer<OrderStatusChangedEvent>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task Consume(ConsumeContext<OrderStatusChangedEvent> context)
    {
        var message = context.Message;

        try
        {
            var deduplicationKey = BuildDeduplicationKey(message);
            var exists = await dbContext.Notifications
                .AsNoTracking()
                .AnyAsync(n =>
                    n.SourceEventId == message.EventId ||
                    n.DeduplicationKey == deduplicationKey,
                    context.CancellationToken);

            if (exists)
            {
                logger.LogInformation(
                    "Notification for OrderStatusChangedEvent {EventId} already exists.",
                    message.EventId);
                return;
            }

            var content = BuildContent(message);
            var dataJson = JsonSerializer.Serialize(new
            {
                message.OrderId,
                message.OldStatus,
                message.NewStatus,
                message.Reason
            }, JsonOptions);

            dbContext.Notifications.Add(NotificationMessage.Create(
                message.CustomerId,
                message.EventId,
                deduplicationKey,
                content.Type,
                content.Title,
                content.Message,
                dataJson,
                message.OccurredAt));

            await dbContext.SaveChangesAsync(context.CancellationToken);

            logger.LogInformation(
                "Created notification for customer {CustomerId}, order {OrderId}, status {NewStatus}.",
                message.CustomerId,
                message.OrderId,
                message.NewStatus);
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Failed to create notification for OrderStatusChangedEvent {EventId}, Order {OrderId}.",
                message.EventId,
                message.OrderId);
            throw;
        }
    }

    private static string BuildDeduplicationKey(OrderStatusChangedEvent message)
        => $"order-status:{message.OrderId:N}:{message.OldStatus}:{message.NewStatus}:{HashReason(message.Reason)}";

    private static string HashReason(string reason)
    {
        var normalizedReason = string.IsNullOrWhiteSpace(reason)
            ? "none"
            : reason.Trim().ToLowerInvariant();

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(normalizedReason));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static NotificationContent BuildContent(OrderStatusChangedEvent message)
        => message.NewStatus switch
        {
            "PaymentPending" => new NotificationContent(
                "PaymentRequired",
                "Đơn hàng đang chờ thanh toán",
                $"Đơn hàng #{ShortId(message.OrderId)} đã giữ kho thành công. Vui lòng hoàn tất thanh toán."),

            "Processing" => new NotificationContent(
                "OrderProcessing",
                "Đơn hàng đang được xử lý",
                $"Đơn hàng #{ShortId(message.OrderId)} đã được xác nhận và đang được xử lý."),

            "Shipped" => new NotificationContent(
                "OrderShipped",
                "Đơn hàng đang được giao",
                $"Đơn hàng #{ShortId(message.OrderId)} đã được chuyển cho đơn vị vận chuyển."),

            "Delivered" => new NotificationContent(
                "OrderDelivered",
                "Đơn hàng đã giao thành công",
                $"Đơn hàng #{ShortId(message.OrderId)} đã được xác nhận giao thành công."),

            "ReturnRequested" => new NotificationContent(
                "ReturnRequested",
                "Yêu cầu trả hàng đã được ghi nhận",
                $"Yêu cầu trả hàng cho đơn #{ShortId(message.OrderId)} đang chờ xử lý."),

            "Returned" => new NotificationContent(
                "ReturnApproved",
                "Yêu cầu trả hàng đã được duyệt",
                $"Yêu cầu trả hàng cho đơn #{ShortId(message.OrderId)} đã được duyệt."),

            "ReturnRejected" => new NotificationContent(
                "ReturnRejected",
                "Yêu cầu trả hàng bị từ chối",
                $"Yêu cầu trả hàng cho đơn #{ShortId(message.OrderId)} đã bị từ chối."),

            "Cancelled" => new NotificationContent(
                "OrderCancelled",
                "Đơn hàng đã bị hủy",
                $"Đơn hàng #{ShortId(message.OrderId)} đã bị hủy. {message.Reason}".Trim()),

            _ => new NotificationContent(
                "OrderStatusChanged",
                "Trạng thái đơn hàng đã thay đổi",
                $"Đơn hàng #{ShortId(message.OrderId)} chuyển từ {message.OldStatus} sang {message.NewStatus}.")
        };

    private static string ShortId(Guid value)
        => value.ToString("N")[..8];

    private sealed record NotificationContent(string Type, string Title, string Message);
}
