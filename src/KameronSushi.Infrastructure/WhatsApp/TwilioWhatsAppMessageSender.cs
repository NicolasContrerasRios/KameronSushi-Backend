using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using KameronSushi.Application.Abstractions;
using KameronSushi.Application.WhatsApp;
using KameronSushi.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KameronSushi.Infrastructure.WhatsApp;

public sealed class TwilioWhatsAppMessageSender(
    HttpClient httpClient,
    IOptions<TwilioOptions> options,
    ILogger<TwilioWhatsAppMessageSender> logger) : IWhatsAppMessageSender
{
    public async Task<string?> SendAsync(
        string recipientWaId,
        OutgoingWhatsAppMessage message,
        CancellationToken cancellationToken)
    {
        var settings = options.Value;
        if (string.IsNullOrWhiteSpace(settings.AccountSid) ||
            string.IsNullOrWhiteSpace(settings.AuthToken) ||
            string.IsNullOrWhiteSpace(settings.WhatsAppNumber))
        {
            logger.LogError("La configuración de Twilio está incompleta");
            throw new InvalidOperationException(
                "Faltan Twilio:AccountSid, Twilio:AuthToken o Twilio:WhatsAppNumber.");
        }

        var endpoint = $"{settings.ApiBaseUrl.TrimEnd('/')}/2010-04-01/Accounts/{settings.AccountSid}/Messages.json";
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
        var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{settings.AccountSid}:{settings.AuthToken}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["From"] = NormalizeWhatsAppAddress(settings.WhatsAppNumber),
            ["To"] = NormalizeWhatsAppAddress(recipientWaId),
            ["Body"] = RenderAsText(message)
        });

        using var response = await httpClient.SendAsync(request, cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning(
                "Twilio rechazó el mensaje saliente con HTTP {StatusCode}. Respuesta: {ResponseBody}",
                (int)response.StatusCode,
                responseBody);
            throw new HttpRequestException($"Twilio respondió {(int)response.StatusCode}: {responseBody}");
        }

        using var document = JsonDocument.Parse(responseBody);
        var providerMessageId = document.RootElement.TryGetProperty("sid", out var sid)
            ? sid.GetString()
            : null;

        logger.LogInformation(
            "Mensaje de WhatsApp enviado por Twilio. Tipo: {MessageType}; destinatario: {MaskedRecipient}",
            message.GetType().Name,
            MaskWaId(recipientWaId));

        return providerMessageId;
    }

    private static string RenderAsText(OutgoingWhatsAppMessage message) => message switch
    {
        TextWhatsAppMessage text => text.Body,
        ButtonsWhatsAppMessage buttons => RenderOptions(
            buttons.Body,
            buttons.Buttons.Select(button => (button.Title, (string?)null))),
        ListWhatsAppMessage list => RenderOptions(
            list.Body,
            list.Rows.Select(row => (row.Title, row.Description))),
        _ => throw new ArgumentOutOfRangeException(nameof(message))
    };

    private static string RenderOptions(
        string body,
        IEnumerable<(string Title, string? Description)> options)
    {
        var lines = options.Select((option, index) =>
            option.Description is null
                ? $"{index + 1}. {option.Title}"
                : $"{index + 1}. {option.Title} — {option.Description}");
        return $"{body}\n\n{string.Join("\n", lines)}\n\nResponde con el número de una opción.";
    }

    private static string NormalizeWhatsAppAddress(string value)
    {
        var normalized = value.Trim();
        if (normalized.StartsWith("whatsapp:", StringComparison.OrdinalIgnoreCase))
        {
            return normalized;
        }

        return $"whatsapp:{(normalized.StartsWith('+') ? normalized : "+" + normalized)}";
    }

    private static string MaskWaId(string waId) =>
        waId.Length <= 4 ? "****" : $"***{waId[^4..]}";
}
