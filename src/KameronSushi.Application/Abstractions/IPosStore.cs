using KameronSushi.Application.Pos;

namespace KameronSushi.Application.Abstractions;

public interface IPosStore
{
    Task<IReadOnlyList<CatalogProduct>> GetCatalogAsync(CancellationToken cancellationToken);
    Task<CustomerLoyalty?> GetCustomerLoyaltyByPhoneAsync(string phone, CancellationToken cancellationToken);
    Task<CreatedOrder> CreateLocalOrderAsync(CreateLocalOrder command, CancellationToken cancellationToken);
    Task<IReadOnlyList<PosOrderSummary>> GetOrdersAsync(string? status, int limit, CancellationToken cancellationToken);
    Task<PosOrderDetails?> GetOrderAsync(long orderId, CancellationToken cancellationToken);
    Task<IReadOnlyList<KitchenOrderSummary>> GetKitchenOrdersAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<KitchenPerformanceSummary>> GetKitchenPerformanceAsync(CancellationToken cancellationToken);
    Task<bool> MarkOrderReadyAsync(long orderId, CancellationToken cancellationToken);
    Task<KitchenPrintJob?> ClaimKitchenPrintJobAsync(string workerId, CancellationToken cancellationToken);
    Task<bool> CompleteKitchenPrintJobAsync(Guid claimToken, string workerId, CancellationToken cancellationToken);
    Task<bool> FailKitchenPrintJobAsync(Guid claimToken, string workerId, string error, CancellationToken cancellationToken);
    Task<CashShift?> GetCurrentShiftAsync(CancellationToken cancellationToken);
    Task<CashShift> OpenShiftAsync(decimal openingAmount, CancellationToken cancellationToken);
    Task<CashShiftReport?> GetShiftReportAsync(long shiftId, CancellationToken cancellationToken);
    Task<CashMovement?> AddCashMovementAsync(long shiftId, CreateCashMovement movement, CancellationToken cancellationToken);
    Task<CashShiftReport?> CloseShiftAsync(long shiftId, CloseCashShift request, CancellationToken cancellationToken);
}
