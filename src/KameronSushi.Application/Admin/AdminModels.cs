namespace KameronSushi.Application.Admin;

public sealed record AdminCategory(long Id, string Name, bool Active, int Order);

public sealed record AdminProduct(
    long Id, long CategoryId, string Category, string Name, string? Description,
    decimal Price, bool Active, bool Available, bool RequiresConfiguration);

public sealed record SaveAdminProduct(
    long CategoryId, string Name, string? Description, decimal Price,
    bool Active = true, bool Available = true);

public sealed record AdminRewardProduct(
    long RewardProductId, long ProductId, string ProductName, int PointsCost,
    bool Active, int? Stock, int? LimitPerOrder);

public sealed record SaveAdminRewardProduct(
    long ProductId, int PointsCost, bool Active = true,
    int? Stock = null, int? LimitPerOrder = null);
