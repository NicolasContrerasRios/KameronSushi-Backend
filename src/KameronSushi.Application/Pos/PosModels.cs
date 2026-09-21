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
    IReadOnlyList<CreateLocalPayment> Payments);

public sealed record CreateLocalOrderItem(
    long ProductId,
    long? OptionId,
    long? ProductWrapperId,
    long? SauceId,
    int Quantity = 1);

public sealed record CreateLocalPayment(string Method, decimal Amount);

public sealed record CreatedOrder(long OrderId, decimal Total, decimal Paid, decimal Change, DateTimeOffset CreatedAt);

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
    DateTimeOffset CreatedAt,
    IReadOnlyList<string> Products);
