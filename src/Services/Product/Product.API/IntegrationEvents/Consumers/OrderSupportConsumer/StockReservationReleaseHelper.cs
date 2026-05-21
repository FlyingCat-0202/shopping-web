using EventBus.Contracts;
using Microsoft.EntityFrameworkCore;
using Product.Domain.Entities;
using Product.Infrastructure.Data;

namespace Product.API.IntegrationEvents.Consumers.OrderSupportConsumer;

internal static class StockReservationReleaseHelper
{
    public static async Task<int> ReleaseReservedStockAsync(
        ProductDbContext dbContext,
        Guid orderId,
        IEnumerable<OrderItemInfo> items,
        string releaseReason,
        CancellationToken cancellationToken)
    {
        var productIds = items
            .Select(x => x.ProductId)
            .Distinct()
            .ToList();

        if (productIds.Count == 0)
            return 0;

        var reservations = await dbContext.StockReservations
            .Where(r => r.OrderId == orderId && productIds.Contains(r.ProductId))
            .ToListAsync(cancellationToken);

        var reservationByProductId = reservations.ToDictionary(r => r.ProductId);
        var missingReservationIds = productIds
            .Where(productId => !reservationByProductId.ContainsKey(productId))
            .ToList();

        if (missingReservationIds.Count > 0)
        {
            throw new InvalidOperationException(
                $"Không tìm thấy stock reservation cho Order {orderId}, ProductIds: {string.Join(", ", missingReservationIds)}.");
        }

        var releasableReservations = reservations
            .Where(r => r.Status == StockReservationStatus.Reserved)
            .ToList();

        if (releasableReservations.Count == 0)
            return 0;

        var releasableProductIds = releasableReservations.Select(r => r.ProductId).ToList();
        var products = await dbContext.Products
            .Where(p => releasableProductIds.Contains(p.Id))
            .ToListAsync(cancellationToken);

        if (products.Count != releasableProductIds.Count)
        {
            var foundProductIds = products.Select(p => p.Id).ToHashSet();
            var missingProductIds = releasableProductIds
                .Where(productId => !foundProductIds.Contains(productId))
                .ToList();

            throw new InvalidOperationException(
                $"Không tìm thấy sản phẩm để hoàn kho cho Order {orderId}, ProductIds: {string.Join(", ", missingProductIds)}.");
        }

        var productById = products.ToDictionary(p => p.Id);
        foreach (var reservation in releasableReservations)
        {
            productById[reservation.ProductId].StockQuantity += reservation.Quantity;
            reservation.Release(releaseReason);
        }

        return releasableReservations.Count;
    }
}
