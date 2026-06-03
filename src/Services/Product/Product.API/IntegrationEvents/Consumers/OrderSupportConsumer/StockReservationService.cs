using EventBus.Contracts;
using Microsoft.EntityFrameworkCore;
using Product.Domain.Entities;
using Product.Infrastructure.Data;
using ProductEntity = Product.Domain.Entities.Product;

namespace Product.API.IntegrationEvents.Consumers.OrderSupportConsumer;

public interface IStockReservationService
{
    Task<StockReservationResult> ReserveStockAsync(
        Guid orderId,
        IEnumerable<OrderItemInfo> items,
        CancellationToken cancellationToken);

    Task<int> ReleaseReservedStockAsync(
        Guid orderId,
        IEnumerable<OrderItemInfo> items,
        string releaseReason,
        CancellationToken cancellationToken);
}

public class StockReservationService(ProductDbContext dbContext) : IStockReservationService
{
    public async Task<StockReservationResult> ReserveStockAsync(
        Guid orderId,
        IEnumerable<OrderItemInfo> items,
        CancellationToken cancellationToken)
    {
        var requestedItems = BuildRequestedItems(items);
        if (requestedItems.Count == 0)
            return StockReservationResult.Failed("Đơn hàng không có sản phẩm.");

        if (requestedItems.Any(x => x.Quantity <= 0))
            return StockReservationResult.Failed("Số lượng sản phẩm không hợp lệ.");

        var existingReservations = await dbContext.StockReservations
            .Where(r => r.OrderId == orderId)
            .ToListAsync(cancellationToken);

        if (existingReservations.Count > 0)
            return ResolveExistingReservation(orderId, requestedItems, existingReservations);

        var productIds = requestedItems.Select(x => x.ProductId).ToList();
        var products = await dbContext.Products
            .Where(p => productIds.Contains(p.Id) && p.IsActive)
            .ToListAsync(cancellationToken);

        if (products.Count != productIds.Count)
            return StockReservationResult.Failed("Sản phẩm không tồn tại hoặc đã ngừng kinh doanh.");

        var productById = products.ToDictionary(p => p.Id);
        foreach (var item in requestedItems)
        {
            var product = productById[item.ProductId];
            if (item.Quantity > product.StockQuantity)
                return StockReservationResult.Failed($"Sản phẩm {product.Id} không đủ hàng.");
        }

        foreach (var item in requestedItems)
        {
            var product = productById[item.ProductId];
            product.ReserveStock(item.Quantity);

            dbContext.StockReservations.Add(
                StockReservation.Create(
                    orderId,
                    item.ProductId,
                    product.Name,
                    product.ImageUrl,
                    item.Quantity,
                    product.Price));
        }

        return StockReservationResult.Reserved(
            requestedItems.Select(i => ToValidatedOrderItem(i, productById[i.ProductId])).ToList());
    }

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

    private static List<RequestedStockItem> BuildRequestedItems(IEnumerable<OrderItemInfo> items)
        => items
            .GroupBy(x => x.ProductId)
            .Select(g => new RequestedStockItem(g.Key, g.Sum(x => x.Quantity)))
            .ToList();

    private static StockReservationResult ResolveExistingReservation(
        Guid orderId,
        IReadOnlyCollection<RequestedStockItem> requestedItems,
        IReadOnlyCollection<StockReservation> reservations)
    {
        if (!MatchesExistingReservations(requestedItems, reservations))
            throw new InvalidOperationException($"Stock reservation data không khớp với Order {orderId}.");

        if (reservations.All(r => r.Status == StockReservationStatus.Reserved))
            return StockReservationResult.Reserved(reservations.Select(ToValidatedOrderItem).ToList());

        if (reservations.All(r => r.Status == StockReservationStatus.Released))
            return StockReservationResult.Ignored();

        throw new InvalidOperationException($"Stock reservation status không hợp lệ cho Order {orderId}.");
    }

    private static bool MatchesExistingReservations(
        IReadOnlyCollection<RequestedStockItem> requestedItems,
        IReadOnlyCollection<StockReservation> reservations)
    {
        var expectedItems = requestedItems.ToDictionary(x => x.ProductId, x => x.Quantity);
        var existingItems = reservations.ToDictionary(x => x.ProductId, x => x.Quantity);

        return expectedItems.Count == existingItems.Count &&
               expectedItems.All(x => existingItems.TryGetValue(x.Key, out var quantity) && quantity == x.Value);
    }

    private static ValidatedOrderItem ToValidatedOrderItem(RequestedStockItem item, ProductEntity product)
        => new()
        {
            ProductId = item.ProductId,
            ProductName = product.Name,
            ProductImageUrl = product.ImageUrl,
            Quantity = item.Quantity,
            UnitPrice = product.Price
        };

    private static ValidatedOrderItem ToValidatedOrderItem(StockReservation reservation)
        => new()
        {
            ProductId = reservation.ProductId,
            ProductName = reservation.ProductName,
            ProductImageUrl = reservation.ProductImageUrl,
            Quantity = reservation.Quantity,
            UnitPrice = reservation.UnitPrice
        };

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
            productById[reservation.ProductId].ReleaseStock(reservation.Quantity);
            reservation.Release(releaseReason);
        }
    }

    private sealed record RequestedStockItem(Guid ProductId, int Quantity);
}

public enum StockReservationResultStatus
{
    Reserved,
    Failed,
    Ignored
}

public sealed record StockReservationResult(
    StockReservationResultStatus Status,
    IReadOnlyList<ValidatedOrderItem> Items,
    string? FailureReason)
{
    public static StockReservationResult Reserved(IReadOnlyList<ValidatedOrderItem> items)
        => new(StockReservationResultStatus.Reserved, items, null);

    public static StockReservationResult Failed(string reason)
        => new(StockReservationResultStatus.Failed, [], reason);

    public static StockReservationResult Ignored()
        => new(StockReservationResultStatus.Ignored, [], null);
}
