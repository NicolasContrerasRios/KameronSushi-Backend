namespace KameronSushi.Application.Pos;

public sealed record CatalogProduct(
    long Id,
    string Name,
    string Category,
    string? Description,
    decimal Price,
    bool Available,
    IReadOnlyList<CatalogSelection> Selections);

public sealed record CatalogSelection(
    long OptionId,
    long ProductWrapperId,
    long SauceId,
    string Label,
    string Details,
    decimal PriceAdjustment);

public sealed record CreateLocalOrder(
    IReadOnlyList<CreateLocalOrderItem> Items,
    IReadOnlyList<CreateLocalPayment> Payments,
    long? CustomerId = null,
    IReadOnlyList<CreateRewardItem>? Rewards = null);

public sealed record CreateLocalOrderItem(
    long ProductId,
    long? OptionId,
    long? ProductWrapperId,
    long? SauceId,
    int Quantity = 1);

public sealed record CreateLocalPayment(string Method, decimal Amount);

public sealed record CreateRewardItem(long RewardProductId, int Quantity = 1);

public sealed record CreatedOrder(
    long OrderId, decimal Total, decimal Paid, decimal Change,
    DateTimeOffset CreatedAt, int? RemainingPoints = null);

public sealed record CustomerLoyalty(
    long CustomerId, string Name, string Phone, int Points,
    IReadOnlyList<RewardCatalogItem> Rewards);

public sealed record RewardCatalogItem(
    long RewardProductId, long ProductId, string Name, string? Description,
    int PointsCost, int? Stock, int? LimitPerOrder);

public sealed record PosOrderSummary(
    long OrderId,
    string Status,
    string DeliveryType,
    string Channel,
    decimal Total,
    int ItemCount,
    string? CustomerName,
    DateTimeOffset CreatedAt);

public sealed record PosOrderDetails(
    long OrderId,
    string Status,
    string DeliveryType,
    string Channel,
    decimal Subtotal,
    decimal Discount,
    decimal ShippingCost,
    decimal Total,
    string? Notes,
    string? CustomerName,
    string? CustomerPhone,
    DateTimeOffset CreatedAt,
    IReadOnlyList<PosOrderItem> Items,
    IReadOnlyList<PosOrderPayment> Payments);

public sealed record PosOrderItem(
    long DetailId,
    long ProductId,
    string Name,
    int Quantity,
    decimal UnitPrice,
    decimal Subtotal,
    string? Details);

public sealed record PosOrderPayment(
    long PaymentId,
    string Method,
    string Status,
    decimal Amount,
    DateTimeOffset CreatedAt,
    DateTimeOffset? PaidAt);

public sealed record KitchenOrderSummary(
    long OrderId,
    string DeliveryType,
    DateTimeOffset CreatedAt,
    IReadOnlyList<string> Products);

public sealed record KitchenPerformanceSummary(
    string DeliveryType,
    int CompletedOrders,
    double? AverageReadyMinutes);

public sealed record CashShift(
    long ShiftId,
    string Status,
    DateTimeOffset OpenedAt,
    DateTimeOffset? ClosedAt,
    long OpenedByUserId,
    string OpenedByName,
    decimal OpeningAmount);

public sealed record OpenCashShift(decimal OpeningAmount);
public sealed record CloseCashShift(decimal CountedCash, string? Notes);
public sealed record CreateCashMovement(string Type, decimal Amount, string Reason);
public sealed record CashMovement(long MovementId, string Type, decimal Amount, string Reason, DateTimeOffset CreatedAt);
public sealed record ShiftOrderReport(long OrderId, string DeliveryType, decimal Total, DateTimeOffset CreatedAt);
public sealed record CashShiftReport(
    CashShift Shift,
    int OrderCount,
    decimal TotalSales,
    decimal CashSales,
    decimal CardSales,
    decimal EdenredSales,
    decimal OtherSales,
    decimal CashIncome,
    decimal CashWithdrawals,
    decimal CashExpenses,
    decimal ExpectedCash,
    decimal? CountedCash,
    decimal? CashDifference,
    IReadOnlyList<ShiftOrderReport> Orders,
    IReadOnlyList<CashMovement> Movements);
