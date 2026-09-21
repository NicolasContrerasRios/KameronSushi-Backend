using KameronSushi.Application.Pos;

namespace KameronSushi.Application.Abstractions;

public interface IPosStore
{
    Task<IReadOnlyList<CatalogProduct>> GetCatalogAsync(CancellationToken cancellationToken);
    Task<CreatedOrder> CreateLocalOrderAsync(CreateLocalOrder command, CancellationToken cancellationToken);
    Task<IReadOnlyList<PosOrderSummary>> GetOrdersAsync(string? status, int limit, CancellationToken cancellationToken);
    Task<PosOrderDetails?> GetOrderAsync(long orderId, CancellationToken cancellationToken);
    Task<IReadOnlyList<KitchenOrderSummary>> GetKitchenOrdersAsync(CancellationToken cancellationToken);
    Task<bool> MarkOrderReadyAsync(long orderId, CancellationToken cancellationToken);
}
