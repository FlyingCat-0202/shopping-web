using EventBus.Contracts;
using EventBus.Infrastructure;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Order.API.Dtos;
using EventBus.Extensions;
using Order.Domain.Entities;
using Order.Domain.Enums;
using Order.Infrastructure.Data;
using System.Security.Claims;
using OrderEntity = Order.Domain.Entities.Order;

namespace Order.API.Endpoints;

public static class OrderEndpoints
{
    private const string OrderSubmittedRoutingKey = "order-submitted";
    private const string ReleaseStockRoutingKey = "release-stock";
    private const string OrderStatusChangedRoutingKey = "order-status-changed";

    public static void MapOrderEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/order")
                       .WithTags("Orders")
                       .RequireAuthorization();

        // User endpoints
        group.MapPost("/", CreateOrder)
            .AddEndpointFilter<ValidationFilter<CreateOrderRequest>>()
            .WithName("CreateOrder")
            .AddEndpointFilter<IdempotencyFilter>();
        group.MapGet("/", GetOrders)
            .WithName("GetOrders");
        group.MapGet("/{id:guid}", GetOrderById)
            .WithName("GetOrderById");
        group.MapPut("/{id:guid}/cancel", CancelOrder)
            .WithName("CancelOrder")
            .AddEndpointFilter<IdempotencyFilter>();
        group.MapPut("/{id:guid}/return-request", RequestReturn)
            .WithName("RequestReturn")
            .AddEndpointFilter<IdempotencyFilter>();

        // Admin endpoints
        var admin = group.MapGroup("/")
            .RequireAuthorization(EndpointHelpers.AdminOnly);
        admin.MapGet("/admin", GetAllOrders)
            .WithName("GetAdminOrders");
        admin.MapPut("/{id:guid}/return-approve", ApproveReturn)
            .AddEndpointFilter<IdempotencyFilter>()
            .WithName("ApproveReturn");
        admin.MapPut("/{id:guid}/return-reject", RejectReturn)
            .AddEndpointFilter<IdempotencyFilter>()
            .WithName("RejectReturn");
        admin.MapPut("/{id:guid}/ship", ShipOrder)
            .AddEndpointFilter<IdempotencyFilter>()
            .WithName("ShipOrder");
        admin.MapPut("/{id:guid}/deliver", DeliverOrder)
            .AddEndpointFilter<IdempotencyFilter>()
            .WithName("DeliverOrder");
        admin.MapGet("/admin/{id:guid}", GetAdminOrderById)
            .WithName("GetAdminOrderById");
    }

    // ── User Handlers ─────────────────────────────────────────────────────────

    private static async Task<IResult> CreateOrder(
        CreateOrderRequest request, ClaimsPrincipal user,
        OrderDbContext db,
        IPublishEndpoint publishEndpoint,
        ILogger<OrderEntity> logger,
        CancellationToken cancellationToken)
    {
        if (!EndpointHelpers.TryGetCustomerId(user, out var customerId))
            return Results.Unauthorized();

        if (!Enum.TryParse<PaymentMethodType>(request.PaymentMethod, true, out var paymentMethod))
            return Results.BadRequest(new { message = "Phương thức thanh toán không hợp lệ." });

        var items = request.Items
            .GroupBy(i => i.ProductId)
            .Select(g => new OrderItemGrouping(g.Key, g.Sum(x => x.Quantity)))
            .ToList();

        try
        {
            var order = OrderEntity.Create(
                customerId,
                paymentMethod,
                request.ReceiverName, request.PhoneNumber, request.ShippingAddress);

            foreach (var item in items)
                order.AddOrderItem(item.ProductId, 0, item.Quantity);

            db.Orders.Add(order);
            db.OrderTimelineEvents.AddRange(order.Timeline);

            await publishEndpoint.Publish(new OrderSubmittedEvent
            {
                CorrelationId = order.Id,
                OrderId = order.Id,
                CustomerId = customerId,
                PaymentMethod = order.PaymentMethod.ToString(),
                Items = [.. items.Select(i => new OrderItemInfo { ProductId = i.ProductId, Quantity = i.Quantity })]
            }, ctx => ctx.SetRoutingKey(OrderSubmittedRoutingKey), cancellationToken);
            
            await db.SaveChangesAsync(cancellationToken);
            return Results.Accepted($"/api/order/{order.Id}", new { Message = "Đơn hàng đang được xử lý", OrderId = order.Id });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Lỗi khi khởi tạo đơn hàng.");
            return Results.Problem("Có lỗi xảy ra. Vui lòng thử lại sau.");
        }
    }

    private static async Task<IResult> GetOrders(
        ClaimsPrincipal user,
        OrderDbContext db,
        CancellationToken cancellationToken,
        int pageIndex = 0,
        int pageSize = 10)
    {
        if (!EndpointHelpers.TryGetCustomerId(user, out var customerId))
            return Results.Unauthorized();

        pageSize = Math.Clamp(pageSize, 1, 50);
        pageIndex = Math.Max(pageIndex, 0);

        var query = db.Orders.AsNoTracking().Where(o => o.CustomerId == customerId);
        var totalCount = await query.CountAsync(cancellationToken);

        var ordersDb = await query
            .OrderByDescending(o => o.OrderDate)
            .Skip(pageIndex * pageSize).Take(pageSize)
            .Select(o => new { o.Id, o.OrderDate, o.TotalAmount, o.Status, o.PaymentMethod })
            .ToListAsync(cancellationToken);

        var orders = ordersDb.Select(o => new OrderSummaryResponse(
            o.Id, o.OrderDate, o.TotalAmount,
            o.Status.ToString(),
            OrderEntity.ComputePaymentState(o.Status, o.PaymentMethod).ToString(),
            CanCancel(o.Status, o.PaymentMethod),
            o.Status is OrderStatus.Processing or OrderStatus.Shipped or OrderStatus.Delivered
                or OrderStatus.ReturnRequested or OrderStatus.Returned or OrderStatus.ReturnRejected,
            o.Status == OrderStatus.Delivered)).ToList();

        return Results.Ok(new PagedResult<OrderSummaryResponse>(orders, totalCount, pageIndex, pageSize));
    }

    private static async Task<IResult> GetOrderById(
        Guid id,
        ClaimsPrincipal user,
        OrderDbContext db,
        CancellationToken cancellationToken)
    {
        if (!EndpointHelpers.TryGetCustomerId(user, out var customerId))
            return Results.Unauthorized();

        var order = await db.Orders.AsNoTracking()
            .Where(o => o.Id == id && o.CustomerId == customerId)
            .Select(o => new
            {
                o.Id,
                o.OrderDate,
                o.TotalAmount,
                o.DeliveryDetails.ReceiverName,
                o.DeliveryDetails.PhoneNumber,
                o.DeliveryDetails.ShippingAddress,
                o.PaymentMethod,
                o.Status
            })
            .FirstOrDefaultAsync(cancellationToken);
        if (order == null) return Results.NotFound();

        var items = await GetOrderItems(db, id, cancellationToken);
        var timeline = await GetOrderTimeline(db, id, cancellationToken);

        return Results.Ok(new OrderDetailResponse(
            order.Id, order.OrderDate, order.TotalAmount,
            order.ReceiverName, order.PhoneNumber, order.ShippingAddress,
            order.PaymentMethod.ToString(),
            order.Status.ToString(),
            OrderEntity.ComputePaymentState(order.Status, order.PaymentMethod).ToString(),
            CanCancel(order.Status, order.PaymentMethod),
            order.Status is OrderStatus.Processing or OrderStatus.Shipped or OrderStatus.Delivered
                or OrderStatus.ReturnRequested or OrderStatus.Returned or OrderStatus.ReturnRejected,
            order.Status == OrderStatus.Delivered,
            items,
            timeline));
    }

    private static async Task<IResult> CancelOrder(
        Guid id,
        ClaimsPrincipal user,
        OrderDbContext db,
        IPublishEndpoint publishEndpoint,
        CancellationToken cancellationToken)
    {
        if (!EndpointHelpers.TryGetCustomerId(user, out var customerId))
            return Results.Unauthorized();

        var order = await db.Orders.Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.Id == id && o.CustomerId == customerId, cancellationToken);
        if (order == null) return Results.NotFound();

        if (!order.CanCancel())
            return Results.Conflict(new { message = $"Không thể hủy đơn hàng ở trạng thái {order.Status}." });

        if (order.Status == OrderStatus.Processing)
        {
            if (!TryApplyStatusChange(order, order.Cancel, out var oldStatus, out var conflict))
                return conflict!;

            var reason = "Customer cancelled COD order before shipping.";
            AddStatusTimeline(db, order, oldStatus, reason, "Customer");
            await PublishReleaseStock(publishEndpoint, order, reason, cancellationToken);
            await PublishOrderStatusChanged(publishEndpoint, order, oldStatus, reason, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);

            return Results.Ok(new { message = "Hủy đơn hàng thành công.", OrderId = order.Id });
        }

        // PaymentPending vẫn thuộc phần create/payment orchestration, để saga xử lý compensation.
        await publishEndpoint.Publish(new CancelOrderCommand
        {
            CorrelationId = order.Id,
            OrderId = order.Id,
            CustomerId = customerId,
            Reason = "Customer cancelled order."
        }, cancellationToken);

        await db.SaveChangesAsync(cancellationToken);
        return Results.Accepted($"/api/order/{order.Id}", new { message = "Yêu cầu hủy đơn hàng đã được gửi.", OrderId = order.Id });
    }

    private static async Task<IResult> RequestReturn(
        Guid id,
        ClaimsPrincipal user,
        OrderDbContext db,
        IPublishEndpoint publishEndpoint,
        CancellationToken cancellationToken)
    {
        if (!EndpointHelpers.TryGetCustomerId(user, out var customerId))
            return Results.Unauthorized();

        var order = await db.Orders.FirstOrDefaultAsync(o => o.Id == id && o.CustomerId == customerId, cancellationToken);
        if (order == null) return Results.NotFound();

        if (!TryApplyStatusChange(order, order.RequestReturn, out var oldStatus, out var conflict))
            return conflict!;
        AddStatusTimeline(db, order, oldStatus, "Customer requested return.", "Customer");
        await PublishOrderStatusChanged(
            publishEndpoint,
            order,
            oldStatus,
            "Customer requested return.",
            cancellationToken);

        await db.SaveChangesAsync(cancellationToken);
        return Results.Ok(new { message = "Yêu cầu trả hàng đã được ghi nhận.", OrderId = order.Id });
    }

    // ── Admin Handlers ────────────────────────────────────────────────────────

    private static async Task<IResult> GetAllOrders(
        OrderDbContext db,
        CancellationToken cancellationToken,
        int pageIndex = 0,
        int pageSize = 10,
        string? status = null)
    {
        pageSize = Math.Clamp(pageSize, 1, 50);
        pageIndex = Math.Max(pageIndex, 0);

        var query = db.Orders.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(status))
        {
            if (!Enum.TryParse<OrderStatus>(status, true, out var orderStatus))
                return Results.BadRequest(new { message = "Trạng thái đơn hàng không hợp lệ." });

            query = query.Where(o => o.Status == orderStatus);
        }

        var totalCount = await query.CountAsync(cancellationToken);

        var ordersDb = await query
            .OrderByDescending(o => o.OrderDate)
            .Skip(pageIndex * pageSize)
            .Take(pageSize)
            .Select(o => new { o.Id, o.CustomerId, o.OrderDate, o.TotalAmount, o.Status, o.PaymentMethod })
            .ToListAsync(cancellationToken);

        var orders = ordersDb.Select(o => new AdminOrderSummaryResponse(
            o.Id,
            o.CustomerId,
            o.OrderDate,
            o.TotalAmount,
            o.Status.ToString(),
            OrderEntity.ComputePaymentState(o.Status, o.PaymentMethod).ToString(),
            CanCancel(o.Status, o.PaymentMethod),
            o.Status is OrderStatus.Processing or OrderStatus.Shipped or OrderStatus.Delivered
                or OrderStatus.ReturnRequested or OrderStatus.Returned or OrderStatus.ReturnRejected,
            o.Status == OrderStatus.Delivered)).ToList();

        return Results.Ok(new PagedResult<AdminOrderSummaryResponse>(orders, totalCount, pageIndex, pageSize));
    }

    private static async Task<IResult> GetAdminOrderById(
        Guid id,
        OrderDbContext db,
        CancellationToken cancellationToken)
    {
        var order = await db.Orders.AsNoTracking()
            .Where(o => o.Id == id)
            .Select(o => new
            {
                o.Id,
                o.CustomerId,
                o.OrderDate,
                o.TotalAmount,
                o.DeliveryDetails.ReceiverName,
                o.DeliveryDetails.PhoneNumber,
                o.DeliveryDetails.ShippingAddress,
                o.PaymentMethod,
                o.Status
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (order == null) return Results.NotFound();

        var items = await GetOrderItems(db, id, cancellationToken);
        var timeline = await GetOrderTimeline(db, id, cancellationToken);

        return Results.Ok(new AdminOrderDetailResponse(
            order.Id,
            order.CustomerId,
            order.OrderDate,
            order.TotalAmount,
            order.ReceiverName,
            order.PhoneNumber,
            order.ShippingAddress,
            order.PaymentMethod.ToString(),
            order.Status.ToString(),
            OrderEntity.ComputePaymentState(order.Status, order.PaymentMethod).ToString(),
            CanCancel(order.Status, order.PaymentMethod),
            order.Status is OrderStatus.Processing or OrderStatus.Shipped or OrderStatus.Delivered
                or OrderStatus.ReturnRequested or OrderStatus.Returned or OrderStatus.ReturnRejected,
            order.Status == OrderStatus.Delivered,
            items,
            timeline));
    }
    private static async Task<IResult> ApproveReturn(
        Guid id,
        OrderDbContext db,
        IPublishEndpoint publishEndpoint,
        CancellationToken cancellationToken)
    {
        var order = await db.Orders.Include(o => o.Items).FirstOrDefaultAsync(o => o.Id == id, cancellationToken);
        if (order == null) return Results.NotFound();

        if (!TryApplyStatusChange(order, order.ApproveReturn, out var oldStatus, out var conflict))
            return conflict!;
        AddStatusTimeline(db, order, oldStatus, "Admin approved return.", "Admin");

        await publishEndpoint.Publish(new ReleaseStockCommand
        {
            CorrelationId = order.Id,
            OrderId = order.Id,
            CustomerId = order.CustomerId,
            Reason = "Admin approved return.",
            Items = [.. order.Items.Select(od => new OrderItemInfo
            {
                ProductId = od.ProductId,
                Quantity = od.Quantity
            })]
        }, cancellationToken);

        await PublishOrderStatusChanged(
            publishEndpoint,
            order,
            oldStatus,
            "Admin approved return.",
            cancellationToken);

        await db.SaveChangesAsync(cancellationToken);
        return Results.Ok(new { message = "Đã duyệt trả hàng thành công.", OrderId = order.Id });
    }

    private static async Task<IResult> RejectReturn(
        Guid id,
        OrderDbContext db,
        IPublishEndpoint publishEndpoint,
        CancellationToken cancellationToken)
    {
        var order = await db.Orders.FirstOrDefaultAsync(o => o.Id == id, cancellationToken);
        if (order == null) return Results.NotFound();
        if (!TryApplyStatusChange(order, order.RejectReturn, out var oldStatus, out var conflict))
            return conflict!;
        AddStatusTimeline(db, order, oldStatus, "Admin rejected return.", "Admin");
        await PublishOrderStatusChanged(
            publishEndpoint,
            order,
            oldStatus,
            "Admin rejected return.",
            cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return Results.Ok(new { message = "Yêu cầu trả hàng đã bị từ chối.", OrderId = order.Id });
    }

    private static async Task<IResult> ShipOrder(
        Guid id,
        OrderDbContext db,
        IPublishEndpoint publishEndpoint,
        CancellationToken cancellationToken)
    {
        var order = await db.Orders.FirstOrDefaultAsync(o => o.Id == id, cancellationToken);
        if (order == null) return Results.NotFound();
        if (!TryApplyStatusChange(order, order.Ship, out var oldStatus, out var conflict))
            return conflict!;
        AddStatusTimeline(db, order, oldStatus, "Admin shipped order.", "Admin");
        await PublishOrderStatusChanged(
            publishEndpoint,
            order,
            oldStatus,
            "Admin shipped order.",
            cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return Results.Ok(new { message = "Đơn hàng đã được chuyển sang trạng thái đang giao.", OrderId = order.Id });
    }

    private static async Task<IResult> DeliverOrder(
        Guid id,
        OrderDbContext db,
        IPublishEndpoint publishEndpoint,
        CancellationToken cancellationToken)
    {
        var order = await db.Orders.FirstOrDefaultAsync(o => o.Id == id, cancellationToken);
        if (order == null) return Results.NotFound();
        if (!TryApplyStatusChange(order, order.Deliver, out var oldStatus, out var conflict))
            return conflict!;
        AddStatusTimeline(db, order, oldStatus, "Admin marked order as delivered.", "Admin");
        await PublishOrderStatusChanged(
            publishEndpoint,
            order,
            oldStatus,
            "Admin marked order as delivered.",
            cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return Results.Ok(new { message = "Đơn hàng đã được xác nhận giao thành công.", OrderId = order.Id });
    }

    private static Task PublishOrderStatusChanged(
        IPublishEndpoint publishEndpoint,
        OrderEntity order,
        OrderStatus oldStatus,
        string reason,
        CancellationToken cancellationToken)
        => publishEndpoint.Publish(new OrderStatusChangedEvent
        {
            CorrelationId = order.Id,
            OrderId = order.Id,
            CustomerId = order.CustomerId,
            OldStatus = oldStatus.ToString(),
            NewStatus = order.Status.ToString(),
            Reason = reason
        }, ctx => ctx.SetRoutingKey(OrderStatusChangedRoutingKey), cancellationToken);

    private static Task PublishReleaseStock(
        IPublishEndpoint publishEndpoint,
        OrderEntity order,
        string reason,
        CancellationToken cancellationToken)
        => publishEndpoint.Publish(new ReleaseStockCommand
        {
            CorrelationId = order.Id,
            OrderId = order.Id,
            CustomerId = order.CustomerId,
            Reason = reason,
            Items = [.. order.Items.Select(od => new OrderItemInfo
            {
                ProductId = od.ProductId,
                Quantity = od.Quantity
            })]
        }, ctx => ctx.SetRoutingKey(ReleaseStockRoutingKey), cancellationToken);

    private static void AddStatusTimeline(
        OrderDbContext db,
        OrderEntity order,
        OrderStatus oldStatus,
        string reason,
        string source)
    {
        if (oldStatus == order.Status)
            return;

        var timelineEvent = order.AddTimelineEvent(
            order.Status,
            $"Order {order.Status}",
            reason,
            source);

        db.OrderTimelineEvents.Add(timelineEvent);
    }

    private static Task<List<OrderItemResponse>> GetOrderItems(
        OrderDbContext db,
        Guid orderId,
        CancellationToken cancellationToken)
        => db.OrderDetails.AsNoTracking()
            .Where(od => od.OrderId == orderId)
            .OrderBy(od => od.ProductId)
            .Select(od => new OrderItemResponse(
                od.ProductId,
                string.IsNullOrWhiteSpace(od.ProductName)
                    ? $"Product {od.ProductId.ToString("N").Substring(0, 8)}"
                    : od.ProductName,
                od.ProductImageUrl,
                od.Quantity,
                od.UnitPrice))
            .ToListAsync(cancellationToken);

    private static Task<List<OrderTimelineItemResponse>> GetOrderTimeline(
        OrderDbContext db,
        Guid orderId,
        CancellationToken cancellationToken)
        => db.OrderTimelineEvents.AsNoTracking()
            .Where(t => t.OrderId == orderId)
            .OrderBy(t => t.OccurredAt)
            .Select(t => new OrderTimelineItemResponse(
                t.Id,
                t.Status,
                t.Title,
                t.Description,
                t.Source,
                t.OccurredAt))
            .ToListAsync(cancellationToken);
    private static bool TryApplyStatusChange(
        OrderEntity order,
        Action changeStatus,
        out OrderStatus oldStatus,
        out IResult? conflict)
    {
        oldStatus = order.Status;
        conflict = null;

        try
        {
            changeStatus();
            return true;
        }
        catch (InvalidOperationException ex)
        {
            conflict = Results.Conflict(new { message = ex.Message });
            return false;
        }
    }

    private static bool CanCancel(OrderStatus status, PaymentMethodType paymentMethod)
        => status == OrderStatus.PaymentPending ||
           (status == OrderStatus.Processing && paymentMethod == PaymentMethodType.COD);

    private sealed record OrderItemGrouping(Guid ProductId, int Quantity);
}
