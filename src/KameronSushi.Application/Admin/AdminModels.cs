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

public sealed record AdminRewardProduct(
    long RewardProductId, long ProductId, string ProductName, int PointsCost,
    bool Active, int? Stock, int? LimitPerOrder);

public sealed record SaveAdminRewardProduct(
    long ProductId, int PointsCost, bool Active = true,
    int? Stock = null, int? LimitPerOrder = null);
