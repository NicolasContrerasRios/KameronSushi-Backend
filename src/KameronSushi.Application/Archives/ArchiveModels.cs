using KameronSushi.Application.Admin;
using KameronSushi.Application.Pos;

namespace KameronSushi.Application.Archives;

public sealed record ArchiveMonth(
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    bool EligibleForMessageCleanup,
    bool EligibleForPurge,
    bool ReadyForPurge,
    DateTimeOffset? ArchivedAt,
    string? ArchiveSha256,
    DateTimeOffset? MessagesPurgedAt,
    DateTimeOffset? PurgedAt);

public sealed record MonthlyDataArchive(
    int SchemaVersion,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    DateTimeOffset GeneratedAt,
    IReadOnlyList<ArchivedShift> Shifts,
    IReadOnlyList<ArchivedOrder> Orders,
    IReadOnlyList<ArchivedWhatsAppMessage> WhatsAppMessages);

public sealed record ArchivedShift(AdminShiftSummary Summary, CashShiftReport Report);
public sealed record ArchivedOrder(
    PosOrderDetails Details,
    IReadOnlyList<OrderStateChange> History,
    ArchivedDelivery? Delivery,
    IReadOnlyList<ArchivedRollConfiguration> RollConfigurations,
    IReadOnlyList<ArchivedCheckout> Checkouts,
    IReadOnlyList<ArchivedPaymentEvent> PaymentEvents);
public sealed record ArchivedDelivery(
    string RecipientName, string RecipientPhone, string Street, string Number,
    string? Apartment, string County, string? Reference, string Status,
    DateTimeOffset? DispatchedAt, DateTimeOffset? DeliveredAt,
    DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);
public sealed record ArchivedRollConfiguration(long DetailId, string SnapshotJson);
public sealed record ArchivedCheckout(
    long CheckoutId, string PreferenceId, string MerchantReference, string PaymentUrl,
    decimal Amount, string Currency, DateTimeOffset CreatedAt, DateTimeOffset? ExpiresAt);
public sealed record ArchivedPaymentEvent(
    long EventId, long? PaymentId, string Provider, string ProviderEventId, string Type,
    string ProviderResourceId, string ProcessingStatus, int Attempts, string? LastError,
    DateTimeOffset ReceivedAt, DateTimeOffset? ProcessedAt);
public sealed record ArchivedWhatsAppMessage(
    long MessageId,
    long ConversationId,
    long? OrderId,
    string? ProviderMessageId,
    string Direction,
    string Type,
    string? Content,
    string? TemplateName,
    string Status,
    string? SelectedOption,
    string? ErrorDetail,
    DateTimeOffset? SentAt,
    DateTimeOffset? DeliveredAt,
    DateTimeOffset? ReadAt,
    DateTimeOffset CreatedAt);

public sealed record ArchivePurgeResult(
    DateOnly PeriodStart,
    int DeletedOrders,
    int DeletedShifts,
    int DeletedWhatsAppMessages,
    DateTimeOffset? MessagesPurgedAt,
    DateTimeOffset? PurgedAt);
