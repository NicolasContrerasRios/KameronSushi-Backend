using System.Text.Json;
using KameronSushi.Application.Abstractions;
using KameronSushi.Application.Features.WhatsApp;
using KameronSushi.Application.WhatsApp;
using KameronSushi.Infrastructure.Configuration;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace KameronSushi.Api.Controllers;

[ApiController]
[Route("webhooks/whatsapp")]
public sealed class WhatsAppWebhookController(
    WhatsAppConversationService conversationService,
    IWebhookSignatureValidator signatureValidator,
    IOptions<WhatsAppOptions> options,
    ILogger<WhatsAppWebhookController> logger) : ControllerBase
{
    [HttpGet]
    [Produces("text/plain")]
    public IActionResult Verify(
        [FromQuery(Name = "hub.mode")] string? mode,
        [FromQuery(Name = "hub.verify_token")] string? verifyToken,
        [FromQuery(Name = "hub.challenge")] string? challenge)
    {
        if (mode == "subscribe" &&
            !string.IsNullOrWhiteSpace(options.Value.VerifyToken) &&
            FixedEquals(verifyToken, options.Value.VerifyToken))
        {
            return Content(challenge ?? string.Empty, "text/plain");
        }

        return StatusCode(StatusCodes.Status403Forbidden);
    }

    [HttpPost]
    public async Task<IActionResult> Receive(CancellationToken cancellationToken)
    {
        using var memory = new MemoryStream();
        await Request.Body.CopyToAsync(memory, cancellationToken);
        var payload = memory.ToArray();

        if (!signatureValidator.IsValid(payload, Request.Headers["X-Hub-Signature-256"].FirstOrDefault()))
        {
            logger.LogWarning("Se rechazó un webhook de WhatsApp con firma inválida");
            return Unauthorized();
        }

        using var document = JsonDocument.Parse(payload);
        foreach (var value in EnumerateValues(document.RootElement))
        {
            await ProcessStatusesAsync(value, cancellationToken);
            await ProcessMessagesAsync(value, cancellationToken);
        }

        return Ok();
    }

    private async Task ProcessMessagesAsync(JsonElement value, CancellationToken cancellationToken)
    {
        if (!value.TryGetProperty("messages", out var messages) || messages.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        var contactNames = ReadContactNames(value);
        foreach (var message in messages.EnumerateArray())
        {
            var providerId = GetString(message, "id");
            var from = GetString(message, "from");
            var type = GetString(message, "type") ?? "unknown";
            if (string.IsNullOrWhiteSpace(providerId) || string.IsNullOrWhiteSpace(from))
            {
                continue;
            }

            var (text, selectionId, selectionTitle) = ReadContent(message, type);
            var contactName = contactNames.GetValueOrDefault(from) ?? from;
            await conversationService.ProcessAsync(new IncomingWhatsAppMessage(
                providerId,
                from,
                from,
                contactName,
                type,
                text,
                selectionId,
                selectionTitle), cancellationToken);
        }
    }

    private async Task ProcessStatusesAsync(JsonElement value, CancellationToken cancellationToken)
    {
        if (!value.TryGetProperty("statuses", out var statuses) || statuses.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (var status in statuses.EnumerateArray())
        {
            var providerId = GetString(status, "id");
            var state = GetString(status, "status");
            if (string.IsNullOrWhiteSpace(providerId) || string.IsNullOrWhiteSpace(state))
            {
                continue;
            }

            var occurredAt = DateTimeOffset.UtcNow;
            if (long.TryParse(GetString(status, "timestamp"), out var unixSeconds))
            {
                occurredAt = DateTimeOffset.FromUnixTimeSeconds(unixSeconds);
            }
            await conversationService.UpdateStatusAsync(providerId, state, occurredAt, cancellationToken);
        }
    }

    private static IEnumerable<JsonElement> EnumerateValues(JsonElement root)
    {
        if (!root.TryGetProperty("entry", out var entries) || entries.ValueKind != JsonValueKind.Array)
        {
            yield break;
        }

        foreach (var entry in entries.EnumerateArray())
        {
            if (!entry.TryGetProperty("changes", out var changes) || changes.ValueKind != JsonValueKind.Array)
            {
                continue;
            }
            foreach (var change in changes.EnumerateArray())
            {
                if (change.TryGetProperty("value", out var value))
                {
                    yield return value;
                }
            }
        }
    }

    private static Dictionary<string, string> ReadContactNames(JsonElement value)
    {
        var result = new Dictionary<string, string>();
        if (!value.TryGetProperty("contacts", out var contacts) || contacts.ValueKind != JsonValueKind.Array)
        {
            return result;
        }

        foreach (var contact in contacts.EnumerateArray())
        {
            var waId = GetString(contact, "wa_id");
            var name = contact.TryGetProperty("profile", out var profile) ? GetString(profile, "name") : null;
            if (!string.IsNullOrWhiteSpace(waId) && !string.IsNullOrWhiteSpace(name))
            {
                result[waId] = name;
            }
        }
        return result;
    }

    private static (string? Text, string? SelectionId, string? SelectionTitle) ReadContent(JsonElement message, string type)
    {
        if (type == "text" && message.TryGetProperty("text", out var text))
        {
            return (GetString(text, "body"), null, null);
        }

        if (type == "interactive" && message.TryGetProperty("interactive", out var interactive))
        {
            foreach (var propertyName in new[] { "button_reply", "list_reply" })
            {
                if (interactive.TryGetProperty(propertyName, out var reply))
                {
                    return (null, GetString(reply, "id"), GetString(reply, "title"));
                }
            }
        }

        if (type == "button" && message.TryGetProperty("button", out var button))
        {
            return (null, GetString(button, "payload"), GetString(button, "text"));
        }

        return (null, null, null);
    }

    private static string? GetString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;

    private static bool FixedEquals(string? supplied, string expected)
    {
        if (supplied is null)
        {
            return false;
        }
        var suppliedBytes = System.Text.Encoding.UTF8.GetBytes(supplied);
        var expectedBytes = System.Text.Encoding.UTF8.GetBytes(expected);
        return suppliedBytes.Length == expectedBytes.Length &&
               System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(suppliedBytes, expectedBytes);
    }
}
