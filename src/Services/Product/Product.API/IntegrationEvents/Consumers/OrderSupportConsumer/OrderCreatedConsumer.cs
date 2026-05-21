using EventBus.Contracts;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Product.Domain.Entities;
using Product.Infrastructure.Data;

namespace Product.API.IntegrationEvents.Consumers.OrderSupportConsumer;

public class OrderCreatedConsumer(ProductDbContext dbContext, ILogger<OrderCreatedConsumer> logger) : IConsumer<OrderCreatedEvent>
{
    public async Task Consume(ConsumeContext<OrderCreatedEvent> context)
    {
        var message = context.Message;
        try
        {
            if (logger.IsEnabled(LogLevel.Information))
            {
                logger.LogInformation("Nhận yêu cầu trừ kho cho Order {OrderId}", message.OrderId);
            }

            // ── Gom sản phẩm từ message ───────────────────────
            var orderItems = message.Items
                .GroupBy(x => x.ProductId)
                .Select(g => new
                {
                    ProductId = g.Key,
                    Quantity = g.Sum(x => x.Quantity)
                })
                .ToList();
            // ── Nếu đơn hàng không có sản phẩm thì khỏi giữ kho ───────────────────────
            if (orderItems.Count == 0)
            {
                logger.LogWarning("Order {OrderId} không có sản phẩm để trừ kho.", message.OrderId);
                await context.Publish(new StockReservationFailedEvent
                {
                    OrderId = message.OrderId,
                    Reason = "Đơn hàng không có sản phẩm."
                }, context.CancellationToken);
                await dbContext.SaveChangesAsync(context.CancellationToken);
                return;
            }

            // ── Kiểm tra xem Order này đã có stock reservation chưa ───────────────────────
            var existingReservations = await dbContext.StockReservations
                .Where(r => r.OrderId == message.OrderId)
                .ToListAsync(context.CancellationToken);

            // ── Nếu đã có reservation rồi thì phải đảm bảo data khớp và không bị duplicate event ───────────────────────
            if (existingReservations.Count > 0)
            {
                var expectedItems = orderItems.ToDictionary(x => x.ProductId, x => x.Quantity);
                var existingItems = existingReservations.ToDictionary(x => x.ProductId, x => x.Quantity);

                if (expectedItems.Count != existingItems.Count ||
                    expectedItems.Any(x => !existingItems.TryGetValue(x.Key, out var quantity) || quantity != x.Value))
                {
                    throw new InvalidOperationException($"Stock reservation data không khớp với Order {message.OrderId}.");
                }

                // ── Order đã được xử lý ───────────────────────
                if (existingReservations.All(r => r.Status == StockReservationStatus.Reserved) ||
                    existingReservations.All(r => r.Status == StockReservationStatus.Released))
                {
                    logger.LogInformation("Order {OrderId} đã có stock reservation, bỏ qua duplicate OrderCreatedEvent.", message.OrderId);
                    return;
                }

                throw new InvalidOperationException($"Stock reservation status không hợp lệ cho Order {message.OrderId}.");
            }

            // ── Kiểm tra tồn kho và tạo stock reservation ───────────────────────
            var productIds = orderItems.Select(x => x.ProductId).ToList();
            var products = await dbContext.Products
                .Where(p => productIds.Contains(p.Id) && p.IsActive)
                .ToListAsync(context.CancellationToken);

            // ── Nếu có sản phẩm nào không tồn tại hoặc ngừng kinh doanh thì phải trả về lỗi ───────────────────────
            if (products.Count != productIds.Count)
            {
                logger.LogWarning("Không tìm thấy một số sản phẩm cho Order {OrderId}", message.OrderId);
                await context.Publish(new StockReservationFailedEvent
                {
                    OrderId = message.OrderId,
                    Reason = "Sản phẩm không tồn tại hoặc đã ngừng kinh doanh."
                }, context.CancellationToken);
                await dbContext.SaveChangesAsync(context.CancellationToken);
                return;
            }

            var productById = products.ToDictionary(p => p.Id);
            // ── Kiểm tra từng sản phẩm xem có đủ hàng không ───────────────────────
            foreach (var item in orderItems)
            {
                var p = productById[item.ProductId];
                if (item.Quantity <= 0 || item.Quantity > p.StockQuantity)
                {
                    logger.LogWarning("Sản phẩm {ProductId} không đủ hàng cho Order {OrderId}", p.Id, message.OrderId);
                    await context.Publish(new StockReservationFailedEvent
                    {
                        OrderId = message.OrderId,
                        Reason = $"Sản phẩm {p.Id} không đủ hàng."
                    }, context.CancellationToken);
                    await dbContext.SaveChangesAsync(context.CancellationToken);

                    return;
                }
            }

            // ── Trừ kho và tạo stock reservation ───────────────────────
            foreach (var item in orderItems)
            {
                var p = productById[item.ProductId];
                p.StockQuantity -= item.Quantity;

                dbContext.StockReservations.Add(
                    StockReservation.Create(message.OrderId, item.ProductId, item.Quantity));
            }

            await context.Publish(new StockReservedEvent
            {
                OrderId = message.OrderId,
                Items = [.. orderItems.Select(i => new ValidatedOrderItem
                {
                    ProductId = i.ProductId,
                    Quantity = i.Quantity,
                    UnitPrice = productById[i.ProductId].Price
                })]
            }, context.CancellationToken);
            
            // ── Lưu thay đổi vào database ───────────────────────
            await dbContext.SaveChangesAsync(context.CancellationToken);
            if (logger.IsEnabled(LogLevel.Information))
            {
                logger.LogInformation("Trừ kho thành công cho Order {OrderId}", message.OrderId);
            }
        }
        catch (DbUpdateConcurrencyException)
        {
            logger.LogWarning("Tranh chấp dữ liệu kho cho Order {OrderId}", message.OrderId);
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Lỗi kỹ thuật khi giữ kho cho Order {OrderId}", message.OrderId);
            throw;
        }
    }
}
