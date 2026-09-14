using KameronSushi.Application.WhatsApp;

namespace KameronSushi.Application.Abstractions;

public interface IWhatsAppMessageSender
{
    Task<string?> SendAsync(string recipientWaId, OutgoingWhatsAppMessage message, CancellationToken cancellationToken);
}
