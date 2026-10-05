using System.Xml.Linq;
using KameronSushi.Application.Abstractions;
using KameronSushi.Application.WhatsApp;
using Microsoft.Extensions.Logging;

namespace KameronSushi.Infrastructure.WhatsApp;

public sealed class TwilioTwiMlMessageSender(
    ILogger<TwilioTwiMlMessageSender> logger) : IWhatsAppMessageSender
{
    private readonly List<string> messages = [];

    public Task<string?> SendAsync(
        string recipientWaId,
        OutgoingWhatsAppMessage message,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        messages.Add(TwilioMessageTextRenderer.Render(message));
        logger.LogInformation(
            "Respuesta de WhatsApp preparada como TwiML. Tipo: {MessageType}; destinatario: {MaskedRecipient}",
            message.GetType().Name,
            MaskWaId(recipientWaId));
        return Task.FromResult<string?>(null);
    }

    public string BuildResponse()
    {
        var response = new XElement(
            "Response",
            messages.Select(message => new XElement("Message", message)));
        return response.ToString(SaveOptions.DisableFormatting);
    }

    private static string MaskWaId(string waId) =>
        waId.Length <= 4 ? "****" : $"***{waId[^4..]}";
}
