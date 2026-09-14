using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using KameronSushi.Application.Abstractions;
using KameronSushi.Application.WhatsApp;
using KameronSushi.Infrastructure.Configuration;
using Microsoft.Extensions.Options;

namespace KameronSushi.Infrastructure.WhatsApp;

public sealed class MetaWhatsAppMessageSender(
    HttpClient httpClient,
    IOptions<WhatsAppOptions> options) : IWhatsAppMessageSender
{
    public async Task<string?> SendAsync(string recipientWaId, OutgoingWhatsAppMessage message, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        if (string.IsNullOrWhiteSpace(settings.AccessToken) || string.IsNullOrWhiteSpace(settings.PhoneNumberId))
        {
            throw new InvalidOperationException("Faltan WhatsApp:AccessToken o WhatsApp:PhoneNumberId.");
        }

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"{settings.GraphApiBaseUrl.TrimEnd('/')}/{settings.ApiVersion}/{settings.PhoneNumberId}/messages");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.AccessToken);
        request.Content = JsonContent.Create(BuildPayload(recipientWaId, message));

        using var response = await httpClient.SendAsync(request, cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"Meta respondió {(int)response.StatusCode}: {responseBody}");
        }

        using var document = JsonDocument.Parse(responseBody);
        return document.RootElement.TryGetProperty("messages", out var messages) && messages.GetArrayLength() > 0 &&
               messages[0].TryGetProperty("id", out var id)
            ? id.GetString()
            : null;
    }

    private static object BuildPayload(string recipient, OutgoingWhatsAppMessage message) => message switch
    {
        TextWhatsAppMessage text => new
        {
            messaging_product = "whatsapp",
            recipient_type = "individual",
            to = recipient,
            type = "text",
            text = new { preview_url = false, body = text.Body }
        },
        ButtonsWhatsAppMessage buttons => new
        {
            messaging_product = "whatsapp",
            recipient_type = "individual",
            to = recipient,
            type = "interactive",
            interactive = new
            {
                type = "button",
                body = new { text = buttons.Body },
                action = new
                {
                    buttons = buttons.Buttons.Select(button => new
                    {
                        type = "reply",
                        reply = new { id = button.Id, title = button.Title }
                    })
                }
            }
        },
        ListWhatsAppMessage list => new
        {
            messaging_product = "whatsapp",
            recipient_type = "individual",
            to = recipient,
            type = "interactive",
            interactive = new
            {
                type = "list",
                body = new { text = list.Body },
                action = new
                {
                    button = list.ButtonText,
                    sections = new[]
                    {
                        new
                        {
                            title = list.SectionTitle,
                            rows = list.Rows.Select(row => new
                            {
                                id = row.Id,
                                title = row.Title,
                                description = row.Description
                            })
                        }
                    }
                }
            }
        },
        _ => throw new ArgumentOutOfRangeException(nameof(message))
    };
}
