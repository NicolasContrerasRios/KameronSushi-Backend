namespace KameronSushi.Application.Customers;

public sealed record RegisterCustomer(string Name, string Phone, string Pin, string? Email, string? DeviceName);
public sealed record LoginCustomer(string Phone, string Pin, string? DeviceName);
public sealed record LoginCustomerWithGoogle(string IdToken, string Phone, string? DeviceName);
public sealed record VerifiedGoogleIdentity(string Subject, string Email, string Name);
public sealed record CustomerSession(string Token, DateTimeOffset ExpiresAt, CustomerProfile Customer);
public sealed record CustomerProfile(long CustomerId, string Name, string Phone, string? Email, int Points);
public sealed record CustomerPromotion(long PromotionId, long ProductId, string Name, string? Description,
    decimal Price, string? IncludedProducts, int IncludedSauces, bool Pickup, bool Delivery);
public sealed record CustomerHome(bool IsOpen, CustomerProfile? Customer,
    IReadOnlyList<CustomerPromotion> Promotions, IReadOnlyList<CustomerReward> Rewards);
public sealed record CustomerReward(long RewardProductId, long ProductId, string Name, string? Description,
    int PointsCost, int? Stock, int? LimitPerOrder, bool CanRedeem);
