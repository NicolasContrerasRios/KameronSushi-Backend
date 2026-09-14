using KameronSushi.Application.WhatsApp;

namespace KameronSushi.Application.Abstractions;

public interface IWhatsAppStore
{
    Task<ConversationRegistration> RegisterInboundAsync(IncomingWhatsAppMessage message, CancellationToken cancellationToken);
    Task MarkInboundProcessedAsync(string providerMessageId, CancellationToken cancellationToken);
    Task UpdateConversationAsync(long conversationId, string step, IReadOnlyDictionary<string, string?> contextChanges, CancellationToken cancellationToken);
    Task<IReadOnlyDictionary<string, string>> GetConversationContextAsync(long conversationId, CancellationToken cancellationToken);
    Task<IReadOnlyList<MenuCategory>> GetCategoriesAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<MenuProduct>> GetProductsAsync(long categoryId, CancellationToken cancellationToken);
    Task<MenuProduct?> GetProductAsync(long productId, CancellationToken cancellationToken);
    Task<IReadOnlyList<RollOption>> GetRollOptionsAsync(long productId, CancellationToken cancellationToken);
    Task<RollOption?> GetRollOptionAsync(long optionId, CancellationToken cancellationToken);
    Task<IReadOnlyList<ProductWrapping>> GetWrappingsAsync(long productId, CancellationToken cancellationToken);
    Task<IReadOnlyList<Sauce>> GetSaucesAsync(CancellationToken cancellationToken);
    Task<CartSummary> AddToCartAsync(long conversationId, IncomingWhatsAppMessage customer, IReadOnlyDictionary<string, string> context, int quantity, CancellationToken cancellationToken);
    Task<CartSummary?> GetCartAsync(long conversationId, CancellationToken cancellationToken);
    Task<long?> ConfirmCashOrderAsync(long conversationId, CancellationToken cancellationToken);
    Task RecordOutgoingAsync(long conversationId, string? providerMessageId, string type, string content, bool sent, string? error, CancellationToken cancellationToken);
    Task UpdateMessageStatusAsync(string providerMessageId, string status, DateTimeOffset occurredAt, CancellationToken cancellationToken);
}
