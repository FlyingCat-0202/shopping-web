using EventBus.Contracts;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Product.Domain.Entities;
using Product.Infrastructure.Data;

namespace Product.API.IntegrationEvents.Consumers.OrderSupportConsumer;

public class ReserveStockConsumer(ProductDbContext dbContext, ILogger<ReserveStockConsumer> logger) : IConsumer<ReserveStockCommand>
{
    public async Task Consume(ConsumeContext<ReserveStockCommand> context)
    {
        var message = context.Message;
        try
        {
            if (logger.IsEnabled(LogLevel.Information))
            {
                logger.LogInformation("Nhận yêu cầu trừ kho cho Order {OrderId}", message.OrderId);
            }

            var orderItems = BuildRequestedItems(message);
            if (orderItems.Count == 0)
            {
                logger.LogWarning("Order {OrderId} không có sản phẩm để trừ kho.", message.OrderId);
                await PublishStockReservationFailedAsync(context, message, "Đơn hàng không có sản phẩm.");
                await dbContext.SaveChangesAsync(context.CancellationToken);
                return;
            }

            var existingReservations = await dbContext.StockReservations
                .Where(r => r.OrderId == message.OrderId)
                .ToListAsync(context.CancellationToken);

            if (existingReservations.Count > 0)
            {
                if (!MatchesExistingReservations(orderItems, existingReservations))
                    throw new InvalidOperationException($"Stock reservation data không khớp với Order {message.OrderId}.");

                if (existingReservations.All(r => r.Status == StockReservationStatus.Reserved))
                {
                    await PublishStockReservedFromReservationsAsync(context, message, message.OrderId, existingReservations);
                    await dbContext.SaveChangesAsync(context.CancellationToken);

                    logger.LogInformation("Order {OrderId} đã giữ kho trước đó, publish lại StockReservedEvent.", message.OrderId);
                    return;
                }

                if (existingReservations.All(r => r.Status == StockReservationStatus.Released))
                {
                    logger.LogInformation("Order {OrderId} đã release stock reservation, bỏ qua duplicate ReserveStockCommand.", message.OrderId);
                    return;
                }

                throw new InvalidOperationException($"Stock reservation status không hợp lệ cho Order {message.OrderId}.");
            }

            var productIds = orderItems.Select(x => x.ProductId).ToList();
            var products = await dbContext.Products
                .Where(p => productIds.Contains(p.Id) && p.IsActive)
                .ToListAsync(context.CancellationToken);

            if (products.Count != productIds.Count)
            {
                logger.LogWarning("Không tìm thấy một số sản phẩm cho Order {OrderId}", message.OrderId);
                await PublishStockReservationFailedAsync(
                    context,
                    message,
                    "Sản phẩm không tồn tại hoặc đã ngừng kinh doanh.");
                await dbContext.SaveChangesAsync(context.CancellationToken);
                return;
            }

            var productById = products.ToDictionary(p => p.Id);
            foreach (var item in orderItems)
            {
                var p = productById[item.ProductId];
                if (item.Quantity <= 0 || item.Quantity > p.StockQuantity)
                {
                    logger.LogWarning("Sản phẩm {ProductId} không đủ hàng cho Order {OrderId}", p.Id, message.OrderId);
                    await PublishStockReservationFailedAsync(context, message, $"Sản phẩm {p.Id} không đủ hàng.");
                    await dbContext.SaveChangesAsync(context.CancellationToken);

                    return;
                }
            }

            foreach (var item in orderItems)
            {
                var p = productById[item.ProductId];
                p.StockQuantity -= item.Quantity;

                dbContext.StockReservations.Add(
                    StockReservation.Create(
                        message.OrderId,
                        item.ProductId,
                        p.Name,
                        p.ImageUrl,
                        item.Quantity,
                        p.Price));
            }

            await PublishStockReservedAsync(context, message, orderItems, productById);
            
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

    private static List<RequestedStockItem> BuildRequestedItems(ReserveStockCommand message)
        => message.Items
            .GroupBy(x => x.ProductId)
            .Select(g => new RequestedStockItem(g.Key, g.Sum(x => x.Quantity)))
            .ToList();

    private static bool MatchesExistingReservations(
        IReadOnlyCollection<RequestedStockItem> orderItems,
        IReadOnlyCollection<StockReservation> reservations)
    {
        var expectedItems = orderItems.ToDictionary(x => x.ProductId, x => x.Quantity);
        var existingItems = reservations.ToDictionary(x => x.ProductId, x => x.Quantity);

        return expectedItems.Count == existingItems.Count &&
               expectedItems.All(x => existingItems.TryGetValue(x.Key, out var quantity) && quantity == x.Value);
    }

    private static Task PublishStockReservationFailedAsync(
        ConsumeContext<ReserveStockCommand> context,
        ReserveStockCommand command,
        string reason)
        => context.Publish(new StockReservationFailedEvent
        {
            CorrelationId = command.CorrelationId,
            OrderId = command.OrderId,
            CustomerId = command.CustomerId,
            Reason = reason
        }, context.CancellationToken);

    private static Task PublishStockReservedAsync(
        ConsumeContext<ReserveStockCommand> context,
        ReserveStockCommand command,
        IReadOnlyCollection<RequestedStockItem> orderItems,
        IReadOnlyDictionary<Guid, Product.Domain.Entities.Product> productById)
        => context.Publish(new StockReservedEvent
        {
            CorrelationId = command.CorrelationId,
            OrderId = command.OrderId,
            CustomerId = command.CustomerId,
            Items = [.. orderItems.Select(i => new ValidatedOrderItem
            {
                ProductId = i.ProductId,
                ProductName = productById[i.ProductId].Name,
                ProductImageUrl = productById[i.ProductId].ImageUrl,
                Quantity = i.Quantity,
                UnitPrice = productById[i.ProductId].Price
            })]
        }, context.CancellationToken);

    private static Task PublishStockReservedFromReservationsAsync(
        ConsumeContext<ReserveStockCommand> context,
        ReserveStockCommand command,
        Guid orderId,
        IReadOnlyCollection<StockReservation> reservations)
        => context.Publish(new StockReservedEvent
        {
            CorrelationId = command.CorrelationId,
            OrderId = orderId,
            CustomerId = command.CustomerId,
            Items = [.. reservations.Select(r => new ValidatedOrderItem
            {
                ProductId = r.ProductId,
                ProductName = r.ProductName,
                ProductImageUrl = r.ProductImageUrl,
                Quantity = r.Quantity,
                UnitPrice = r.UnitPrice
            })]
        }, context.CancellationToken);

    private sealed record RequestedStockItem(Guid ProductId, int Quantity);
}
