namespace KameronSushi.Application.Admin;

public sealed record AdminCategory(long Id, string Name, string? Description, bool Active, int Order);
public sealed record SaveAdminCategory(string Name, string? Description, bool Active, int Order);

public sealed record AdminProduct(
    long Id, long CategoryId, string Category, string Name, string? Description,
    decimal Price, bool Active, bool Available, bool RequiresConfiguration, int Order);

public sealed record SaveAdminProduct(
    long CategoryId, string Name, string? Description, decimal Price,
    bool Active = true, bool Available = true, bool RequiresConfiguration = false, int Order = 0);

public sealed record AdminWrapper(long Id, string Name, bool Active);
public sealed record SaveAdminWrapper(string Name, bool Active = true);
public sealed record AdminSauce(long Id, string Name, string? Description, bool Active);
public sealed record SaveAdminSauce(string Name, string? Description, bool Active = true);
public sealed record AdminProductWrapper(
    long ProductWrapperId, long WrapperId, string Name, decimal PriceAdjustment, bool IsDefault, bool Active);
public sealed record SaveAdminProductWrapper(
    long? ProductWrapperId, long WrapperId, decimal PriceAdjustment, bool IsDefault, bool Active = true);
public sealed record AdminRollOption(
    long OptionId, short Number, string Name, string Ingredients, long? FixedWrapperId, bool Active);
public sealed record SaveAdminRollOption(
    long? OptionId, short Number, string Name, string Ingredients, long? FixedWrapperId, bool Active = true);
public sealed record AdminProductConfiguration(
    long ProductId, IReadOnlyList<AdminProductWrapper> Wrappers, IReadOnlyList<AdminRollOption> Options);
public sealed record SaveAdminProductConfiguration(
    IReadOnlyList<SaveAdminProductWrapper> Wrappers, IReadOnlyList<SaveAdminRollOption> Options);

public sealed record AdminPromotion(
    long PromotionId, long ProductId, string ProductName, decimal Price, string? IncludedProducts,
    int IncludedSauces, DateTimeOffset? StartsAt, DateTimeOffset? EndsAt,
    bool PickupEnabled, bool DeliveryEnabled, bool Active);
public sealed record SaveAdminPromotion(
    long ProductId, string? IncludedProducts, int IncludedSauces,
    DateTimeOffset? StartsAt, DateTimeOffset? EndsAt,
    bool PickupEnabled = true, bool DeliveryEnabled = true, bool Active = true);
public sealed record AdminCustomer(long CustomerId,string Name,string Phone,string? Email,int Points,int AccumulatedPoints,int RedeemedPoints,string Status);
public sealed record AdminPointMovement(long MovementId,string Type,int Points,int PreviousBalance,int ResultingBalance,string? Description,DateTimeOffset CreatedAt);
public sealed record AdjustCustomerPoints(int Points,string Reason);
public sealed record AdminShiftSummary(long ShiftId,string Status,DateTimeOffset OpenedAt,DateTimeOffset? ClosedAt,string OpenedByName,int OrderCount,decimal TotalSales,decimal? CashDifference);
public sealed record AdminKitchenMetric(string DeliveryType,int ActiveOrders,int CompletedOrders,double? AverageMinutes,double? MaximumMinutes,int OverTargetOrders);
public sealed record AdminKitchenProductMetric(string ProductName,int Orders,double AverageMinutes);
public sealed record AdminKitchenDashboard(long? ShiftId,int TargetMinutes,IReadOnlyList<AdminKitchenMetric> DeliveryMetrics,IReadOnlyList<AdminKitchenProductMetric> Products);
public sealed record SaveKitchenTarget(int TargetMinutes);

public sealed record AdminRewardProduct(
    long RewardProductId, long ProductId, string ProductName, int PointsCost,
    bool Active, int? Stock, int? LimitPerOrder);

public sealed record SaveAdminRewardProduct(
    long ProductId, int PointsCost, bool Active = true,
    int? Stock = null, int? LimitPerOrder = null);
