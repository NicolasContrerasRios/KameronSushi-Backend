using KameronSushi.Application.Admin;
using KameronSushi.Application.Pos;

namespace KameronSushi.Application.Archives;

public sealed record ArchiveMonth(
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    bool EligibleForPurge,
    bool ReadyForPurge,
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
public sealed record ArchivedOrder(PosOrderDetails Details, IReadOnlyList<OrderStateChange> History);
public sealed record ArchivedWhatsAppMessage(
    long MessageId,
    long ConversationId,
    long? OrderId,
    string? ProviderMessageId,
    string Direction,
    string Type,
    string? Content,
    string Status,
    DateTimeOffset CreatedAt);

public sealed record ArchivePurgeResult(
    DateOnly PeriodStart,
    int DeletedOrders,
    int DeletedShifts,
    int DeletedWhatsAppMessages,
    DateTimeOffset PurgedAt);
