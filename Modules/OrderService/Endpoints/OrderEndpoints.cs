using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Shopping_web.Data;
using Shopping_web.Modules.OrderService.Models;

namespace Shopping_web.Modules.OrderService.Endpoints;

public static class OrderEndpoints
{
    public static void MapOrderEndpoints(this IEndpointRouteBuilder app)
    {
        // RequireAuthorization() đảm bảo chỉ ai quẹt thẻ JWT hợp lệ mới được gọi các API này
        var group = app.MapGroup("/api/order")
                       .WithTags("Orders")
                       .RequireAuthorization();

        // ====================================================================
        // 1. TẠO ORDER (Mã riêng ngẫu nhiên)
        // ====================================================================
        group.MapPost("/", async (CreateOrderRequest request, ClaimsPrincipal user, OrderDbContext db) =>
        {
            // Lấy ID của user từ thẻ JWT (Không tin tưởng ID do Frontend gửi lên)
            var userIdString = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!Guid.TryParse(userIdString, out Guid customerId))
                return Results.Unauthorized();

            // Khởi tạo Order (Mã Guid.NewGuid() sẽ tự động chạy theo Model của bạn)
            var newOrder = new Order
            {
                CustomerId = customerId,
                PaymentMethod = request.PaymentMethod,
                TotalAmount = request.Items.Sum(i => i.UnitPrice * i.Quantity)
            };

            db.Orders.Add(newOrder);
            await db.SaveChangesAsync();

            var orderDetails = request.Items.Select(i => new OrderDetail
            {
                OrderId = newOrder.Id,
                ProductId = i.ProductId,
                UnitPrice = i.UnitPrice,
                Quantity = i.Quantity
            }).ToList();

            db.OrderDetails.AddRange(orderDetails);
            await db.SaveChangesAsync();

            // Trả về mã Order (Guid) vừa được tạo ngẫu nhiên
            return Results.Ok(new { Message = "Đặt hàng thành công", OrderId = newOrder.Id });
        });

        // ====================================================================
        // 2. XEM BÊN TRONG ORDER (User xem order mình tạo)
        // ====================================================================
        group.MapGet("/{orderId:guid}", async (Guid orderId, ClaimsPrincipal user, OrderDbContext db) =>
        {
            var userIdString = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            var customerId = Guid.Parse(userIdString!);

            var order = await db.Orders
                .FirstOrDefaultAsync(o => o.Id == orderId && o.CustomerId == customerId);
            // Điều kiện o.CustomerId == customerId cực kỳ quan trọng để User A không xem trộm được hàng của User B!

            if (order == null) return Results.NotFound("Không tìm thấy đơn hàng hoặc bạn không có quyền xem.");

            var items = await db.OrderDetails
                .Where(od => od.OrderId == orderId)
                .ToListAsync();

            return Results.Ok(new { order, items });
        });

        // ====================================================================
        // 3. CẬP NHẬT ORDER TỪ NÚT JS (Ví dụ: Hủy đơn hàng)
        // ====================================================================
        group.MapPut("/{orderId:guid}/cancel", async (Guid orderId, ClaimsPrincipal user, OrderDbContext db) =>
        {
            var userIdString = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            var customerId = Guid.Parse(userIdString!);

            var order = await db.Orders.FirstOrDefaultAsync(o => o.Id == orderId && o.CustomerId == customerId);

            if (order == null) return Results.NotFound();

            // Logic thực tế: Chỉ cho phép hủy nếu đơn hàng đang ở trạng thái Pending
            if (order.Status != "Pending")
            {
                return Results.BadRequest("Chỉ có thể hủy đơn hàng đang chờ xử lý.");
            }

            order.Status = "Cancelled";
            await db.SaveChangesAsync();

            return Results.Ok(new { Message = "Đã hủy đơn hàng thành công!", NewStatus = order.Status });
        });
    }
}

// Lớp DTO để nhận dữ liệu từ Frontend gửi lên khi tạo đơn
public class CreateOrderRequest
{
    public string PaymentMethod { get; set; } = string.Empty;
    public List<OrderItemDto> Items { get; set; } = new();
}

public class OrderItemDto
{
    public Guid ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public int Quantity { get; set; }
}