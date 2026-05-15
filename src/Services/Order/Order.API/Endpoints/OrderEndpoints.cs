using EventBus.Contracts;
using EventBus.Infrastructure;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Order.API.Dtos;
using EventBus.Extensions;
using Order.Domain.Enums;
using Order.Infrastructure.Data;
using System.Security.Claims;
using OrderEntity = Order.Domain.Entities.Order;

namespace Order.API.Endpoints;

public static class OrderEndpoints
{
    private const string OrderCreatedRoutingKey = "order-created";
    private const string OrderCancelledRoutingKey = "order-cancelled";
    private const string OrderReturnedRoutingKey = "order-returned";

    public static void MapOrderEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/order")
                       .WithTags("Orders")
                       .RequireAuthorization()
                       .AddEndpointFilter<IdempotencyFilter>();

        // User endpoints
        group.MapPost("/", CreateOrder)
            .AddEndpointFilter<ValidationFilter<CreateOrderRequest>>()
            .WithName("CreateOrder");
        group.MapGet("/", GetOrders).WithName("GetOrders");
        group.MapGet("/{id:guid}", GetOrderById).WithName("GetOrderById");
        group.MapPut("/{id:guid}/cancel", CancelOrder).WithName("CancelOrder");
        group.MapPut("/{id:guid}/return-request", RequestReturn).WithName("RequestReturn");

        // Admin endpoints
        var admin = group.MapGroup("/").RequireAuthorization(EndpointHelpers.AdminOnly);
        admin.MapGet("/admin", GetAllOrders).WithName("GetAdminOrders");
        admin.MapPut("/{id:guid}/return-approve", ApproveReturn);
        admin.MapPut("/{id:guid}/return-reject", RejectReturn);
        admin.MapPut("/{id:guid}/ship", ShipOrder);
        admin.MapPut("/{id:guid}/deliver", DeliverOrder);
    }

    // ── User Handlers ─────────────────────────────────────────────────────────

    private static async Task<IResult> CreateOrder(
        CreateOrderRequest request, ClaimsPrincipal user,
        OrderDbContext db,
        IPublishEndpoint publishEndpoint,
        ILogger<OrderEntity> logger,
        CancellationToken cancellationToken)
    {
        // ── Validation & Authorization ───────────────────────────────────────────────
        if (!EndpointHelpers.TryGetCustomerId(user, out var customerId))
            return Results.Unauthorized();
        
        // ── Grouping Items by ProductId ───────────────────────────────────────────────
        var items = request.Items
            .GroupBy(i => i.ProductId)
            .Select(g => new OrderItemGrouping(g.Key, g.Sum(x => x.Quantity)))
            .ToList();

        try
        {
            // ── Creating Order Entity ───────────────────────────────────────────────
            var order = OrderEntity.Create(
                customerId,
                Enum.Parse<PaymentMethodType>(request.PaymentMethod, true),
                request.ReceiverName, request.PhoneNumber, request.ShippingAddress);

            foreach (var item in items)
                order.AddOrderItem(item.ProductId, 0, item.Quantity);

            db.Orders.Add(order);

            // ── Publishing OrderCreated Event ───────────────────────────────────────────────

            await publishEndpoint.Publish(new OrderCreatedEvent
            {
                OrderId = order.Id,
                CustomerId = customerId,
                Items = [.. items.Select(i => new OrderItemInfo { ProductId = i.ProductId, Quantity = i.Quantity })]
            }, ctx => ctx.SetRoutingKey(OrderCreatedRoutingKey), cancellationToken);
            
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
            o.Status == OrderStatus.Processing,
            o.Status != OrderStatus.Pending && o.Status != OrderStatus.Cancelled,
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
            .FirstOrDefaultAsync(o => o.Id == id && o.CustomerId == customerId, cancellationToken);
        if (order == null) return Results.NotFound();

        var items = await db.OrderDetails.AsNoTracking()
            .Where(od => od.OrderId == order.Id)
            .Select(od => new OrderItemResponse(od.ProductId, od.Quantity, od.UnitPrice))
            .ToListAsync(cancellationToken);

        return Results.Ok(new OrderDetailResponse(
            order.Id, order.OrderDate, order.TotalAmount,
            order.DeliveryDetails.ReceiverName, order.DeliveryDetails.PhoneNumber, order.DeliveryDetails.ShippingAddress,
            order.PaymentMethod.ToString(), order.Status.ToString(), order.PaymentState.ToString(),
            order.CanCancel(), order.CanTrack(), order.CanReturn(), items));
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

        try { order.Cancel(); }
        catch (InvalidOperationException ex) { return Results.Conflict(new { message = ex.Message }); }

        var items = order.Items.Select(od => new OrderItemInfo { ProductId = od.ProductId, Quantity = od.Quantity }).ToList();
        await publishEndpoint.Publish(new OrderCancelledEvent { OrderId = order.Id, Items = items },
            ctx => ctx.SetRoutingKey(OrderCancelledRoutingKey),
            cancellationToken);

        await db.SaveChangesAsync(cancellationToken);
        return Results.Ok(new { message = "Hủy đơn hàng thành công.", OrderId = order.Id });
    }

    private static async Task<IResult> RequestReturn(
        Guid id,
        ClaimsPrincipal user,
        OrderDbContext db,
        CancellationToken cancellationToken)
    {
        if (!EndpointHelpers.TryGetCustomerId(user, out var customerId))
            return Results.Unauthorized();

        var order = await db.Orders.FirstOrDefaultAsync(o => o.Id == id && o.CustomerId == customerId, cancellationToken);
        if (order == null) return Results.NotFound();

        try { order.RequestReturn(); }
        catch (InvalidOperationException ex) { return Results.Conflict(new { message = ex.Message }); }

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
            o.Status == OrderStatus.Processing,
            o.Status != OrderStatus.Pending && o.Status != OrderStatus.Cancelled,
            o.Status == OrderStatus.Delivered)).ToList();

        return Results.Ok(new PagedResult<AdminOrderSummaryResponse>(orders, totalCount, pageIndex, pageSize));
    }

    private static async Task<IResult> ApproveReturn(
        Guid id,
        OrderDbContext db,
        IPublishEndpoint publishEndpoint,
        CancellationToken cancellationToken)
    {
        var order = await db.Orders.Include(o => o.Items).FirstOrDefaultAsync(o => o.Id == id, cancellationToken);
        if (order == null) return Results.NotFound();

        try { order.ApproveReturn(); }
        catch (InvalidOperationException ex) { return Results.Conflict(new { message = ex.Message }); }

        var items = order.Items.Select(od => new OrderItemInfo { ProductId = od.ProductId, Quantity = od.Quantity }).ToList();
        await publishEndpoint.Publish(new OrderReturnedEvent { OrderId = order.Id, Items = items },
            ctx => ctx.SetRoutingKey(OrderReturnedRoutingKey),
            cancellationToken);

        await db.SaveChangesAsync(cancellationToken);
        return Results.Ok(new { message = "Đã duyệt trả hàng thành công.", OrderId = order.Id });
    }

    private static async Task<IResult> RejectReturn(Guid id, OrderDbContext db, CancellationToken cancellationToken)
    {
        var order = await db.Orders.FirstOrDefaultAsync(o => o.Id == id, cancellationToken);
        if (order == null) return Results.NotFound();
        try { order.RejectReturn(); }
        catch (InvalidOperationException ex) { return Results.Conflict(new { message = ex.Message }); }
        await db.SaveChangesAsync(cancellationToken);
        return Results.Ok(new { message = "Yêu cầu trả hàng đã bị từ chối.", OrderId = order.Id });
    }

    private static async Task<IResult> ShipOrder(Guid id, OrderDbContext db, CancellationToken cancellationToken)
    {
        var order = await db.Orders.FirstOrDefaultAsync(o => o.Id == id, cancellationToken);
        if (order == null) return Results.NotFound();
        try { order.Ship(); }
        catch (InvalidOperationException ex) { return Results.Conflict(new { message = ex.Message }); }
        await db.SaveChangesAsync(cancellationToken);
        return Results.Ok(new { message = "Đơn hàng đã được chuyển sang trạng thái đang giao.", OrderId = order.Id });
    }

    private static async Task<IResult> DeliverOrder(Guid id, OrderDbContext db, CancellationToken cancellationToken)
    {
        var order = await db.Orders.FirstOrDefaultAsync(o => o.Id == id, cancellationToken);
        if (order == null) return Results.NotFound();
        try { order.Deliver(); }
        catch (InvalidOperationException ex) { return Results.Conflict(new { message = ex.Message }); }
        await db.SaveChangesAsync(cancellationToken);
        return Results.Ok(new { message = "Đơn hàng đã được xác nhận giao thành công.", OrderId = order.Id });
    }
}
