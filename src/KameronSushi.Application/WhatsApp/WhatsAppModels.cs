namespace KameronSushi.Application.WhatsApp;

public sealed record IncomingWhatsAppMessage(
    string ProviderMessageId,
    string WaId,
    string Phone,
    string ContactName,
    string Type,
    string? Text,
    string? SelectionId,
    string? SelectionTitle);

public sealed record ConversationRegistration(
    long ConversationId,
    string CurrentStep,
    bool IsDuplicate,
    IReadOnlyDictionary<string, string> Context);

public sealed record MenuCategory(long Id, string Name, string? Description);

public sealed record MenuProduct(
    long Id,
    string Name,
    string? Description,
    decimal Price,
    bool RequiresConfiguration);

public sealed record RollOption(long Id, long ProductId, short Number, string Ingredients, long? FixedWrappingId);

public sealed record ProductWrapping(long Id, string Name, decimal ExtraPrice);

public sealed record Sauce(long Id, string Name);

public sealed record CartSummary(long OrderId, IReadOnlyList<string> Lines, decimal Total);

public sealed record WhatsAppButton(string Id, string Title);

public sealed record WhatsAppListRow(string Id, string Title, string? Description = null);

public abstract record OutgoingWhatsAppMessage;

public sealed record TextWhatsAppMessage(string Body) : OutgoingWhatsAppMessage;

public sealed record ButtonsWhatsAppMessage(
    string Body,
    IReadOnlyList<WhatsAppButton> Buttons) : OutgoingWhatsAppMessage;

public sealed record ListWhatsAppMessage(
    string Body,
    string ButtonText,
    string SectionTitle,
    IReadOnlyList<WhatsAppListRow> Rows) : OutgoingWhatsAppMessage;
