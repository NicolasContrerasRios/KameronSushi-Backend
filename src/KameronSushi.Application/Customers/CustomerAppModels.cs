namespace KameronSushi.Application.Customers;

public sealed record RegisterCustomer(string Name, string Phone, string Pin, string? Email, string? DeviceName);
public sealed record LoginCustomer(string Phone, string Pin, string? DeviceName);
public sealed record LoginCustomerWithGoogle(string IdToken, string Phone, string? DeviceName);
public sealed record VerifiedGoogleIdentity(string Subject, string Email, string Name);
public sealed record CustomerSession(string Token, DateTimeOffset ExpiresAt, CustomerProfile Customer);
public sealed record CustomerProfile(long CustomerId, string Name, string Phone, string? Email, int Points);
public sealed record CustomerPromotion(long PromotionId, long ProductId, string Name, string? Description,
    decimal OriginalPrice, decimal OfferPrice, bool Pickup, bool Delivery);
public sealed record CustomerHome(
    bool IsOpen,
    CustomerProfile? Customer,
    IReadOnlyList<CustomerPromotion> Promotions,
    IReadOnlyList<CustomerReward> Rewards,
    int? EstimatedWaitMinMinutes = null,
    int? EstimatedWaitMaxMinutes = null);
public sealed record CustomerReward(long RewardProductId, long ProductId, string Name, string? Description,
    int PointsCost, int? Stock, int? LimitPerOrder, bool CanRedeem);
public sealed record RegisterNotificationDevice(string DeviceToken, string Platform, string? DeviceName);
public sealed record CustomerOrderSummary(
    long OrderId,
    string DeliveryType,
    string Status,
    decimal Total,
    int ItemCount,
    string PaymentMethod,
    DateTimeOffset CreatedAt,
    int? OrderNumber = null);
