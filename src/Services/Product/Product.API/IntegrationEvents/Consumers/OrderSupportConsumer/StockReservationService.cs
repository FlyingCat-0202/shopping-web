using EventBus.Contracts;
using Microsoft.EntityFrameworkCore;
using Product.Domain.Entities;
using Product.Infrastructure.Data;
using ProductEntity = Product.Domain.Entities.Product;

namespace Product.API.IntegrationEvents.Consumers.OrderSupportConsumer;

public interface IStockReservationService
{
    Task<int> ReleaseReservedStockAsync(
        Guid orderId,
        IEnumerable<OrderItemInfo> items,
        string releaseReason,
        CancellationToken cancellationToken);
}

public class StockReservationService(ProductDbContext dbContext) : IStockReservationService
{
    public async Task<int> ReleaseReservedStockAsync(
        Guid orderId,
        IEnumerable<OrderItemInfo> items,
        string releaseReason,
        CancellationToken cancellationToken)
    {
        var productIds = GetDistinctProductIds(items);
        if (productIds.Count == 0)
            return 0;

        var reservations = await LoadReservationsAsync(orderId, productIds, cancellationToken);
        EnsureReservationsExist(orderId, productIds, reservations);

        var releasableReservations = GetReleasableReservations(reservations);
        if (releasableReservations.Count == 0)
            return 0;

        var products = await LoadProductsAsync(orderId, releasableReservations, cancellationToken);

        ReleaseReservations(releasableReservations, products, releaseReason);

        return releasableReservations.Count;
    }

    private static List<Guid> GetDistinctProductIds(IEnumerable<OrderItemInfo> items)
        => items.Select(x => x.ProductId).Distinct().ToList();

    private Task<List<StockReservation>> LoadReservationsAsync(
        Guid orderId,
        IReadOnlyCollection<Guid> productIds,
        CancellationToken cancellationToken)
        => dbContext.StockReservations
            .Where(r => r.OrderId == orderId && productIds.Contains(r.ProductId))
            .ToListAsync(cancellationToken);

    private static void EnsureReservationsExist(
        Guid orderId,
        IReadOnlyCollection<Guid> productIds,
        IReadOnlyCollection<StockReservation> reservations)
    {
        var existingProductIds = reservations.Select(r => r.ProductId).ToHashSet();
        var missingProductIds = productIds
            .Where(productId => !existingProductIds.Contains(productId))
            .ToList();

        if (missingProductIds.Count > 0)
        {
            throw new InvalidOperationException(
                $"Không tìm thấy stock reservation cho Order {orderId}, ProductIds: {string.Join(", ", missingProductIds)}.");
        }
    }

    private static List<StockReservation> GetReleasableReservations(IEnumerable<StockReservation> reservations)
        => reservations
            .Where(r => r.Status == StockReservationStatus.Reserved)
            .ToList();

    private async Task<List<ProductEntity>> LoadProductsAsync(
        Guid orderId,
        IReadOnlyCollection<StockReservation> releasableReservations,
        CancellationToken cancellationToken)
    {
        var productIds = releasableReservations.Select(r => r.ProductId).ToList();
        var products = await dbContext.Products
            .Where(p => productIds.Contains(p.Id))
            .ToListAsync(cancellationToken);

        EnsureProductsExist(orderId, productIds, products);

        return products;
    }

    private static void EnsureProductsExist(
        Guid orderId,
        IReadOnlyCollection<Guid> productIds,
        IReadOnlyCollection<ProductEntity> products)
    {
        var existingProductIds = products.Select(p => p.Id).ToHashSet();
        var missingProductIds = productIds
            .Where(productId => !existingProductIds.Contains(productId))
            .ToList();

        if (missingProductIds.Count > 0)
        {
            throw new InvalidOperationException(
                $"Không tìm thấy sản phẩm để hoàn kho cho Order {orderId}, ProductIds: {string.Join(", ", missingProductIds)}.");
        }
    }

    private static void ReleaseReservations(
        IEnumerable<StockReservation> releasableReservations,
        IEnumerable<ProductEntity> products,
        string releaseReason)
    {
        var productById = products.ToDictionary(p => p.Id);

        foreach (var reservation in releasableReservations)
        {
            productById[reservation.ProductId].StockQuantity += reservation.Quantity;
            reservation.Release(releaseReason);
        }
    }
}
