using KameronSushi.Application.Features.WhatsApp;
using KameronSushi.Application.WhatsApp;
using KameronSushi.Infrastructure.Configuration;
using KameronSushi.Infrastructure.WhatsApp;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace KameronSushi.Api.Controllers;

[ApiController]
[Route("webhooks/twilio/whatsapp")]
public sealed class TwilioWhatsAppWebhookController(
    WhatsAppConversationService conversationService,
    TwilioWebhookSignatureValidator signatureValidator,
    TwilioTwiMlMessageSender twiMlMessageSender,
    IOptions<WhatsAppOptions> whatsAppOptions,
    ILogger<TwilioWhatsAppWebhookController> logger) : ControllerBase
{
    [HttpPost]
    [Consumes("application/x-www-form-urlencoded")]
    public async Task<IActionResult> Receive(CancellationToken cancellationToken)
    {
        var form = await Request.ReadFormAsync(cancellationToken);
        if (!IsValidRequest(form))
        {
            logger.LogWarning("Se rechazó un webhook de Twilio con firma inválida");
            return Unauthorized();
        }

        if (!whatsAppOptions.Value.Provider.Equals("Twilio", StringComparison.OrdinalIgnoreCase))
        {
            logger.LogError(
                "Se recibió un mensaje de Twilio, pero WhatsApp:Provider está configurado como {Provider}",
                whatsAppOptions.Value.Provider);
            return StatusCode(StatusCodes.Status503ServiceUnavailable);
        }

        var providerMessageId = form["MessageSid"].ToString();
        var from = form["From"].ToString();
        if (string.IsNullOrWhiteSpace(providerMessageId) || string.IsNullOrWhiteSpace(from))
        {
            logger.LogWarning("Twilio envió un webhook sin MessageSid o From");
            return BadRequest();
        }

        var waId = NormalizeWaId(form["WaId"].ToString(), from);
        var body = form["Body"].ToString();
        var profileName = form["ProfileName"].ToString();

        logger.LogInformation(
            "Procesando mensaje entrante de Twilio. Tipo: text; remitente: {MaskedSender}",
            MaskWaId(waId));

        await conversationService.ProcessAsync(new IncomingWhatsAppMessage(
            providerMessageId,
            waId,
            waId,
            string.IsNullOrWhiteSpace(profileName) ? waId : profileName,
            "text",
            body,
            null,
            null), twiMlMessageSender, cancellationToken);

        logger.LogInformation(
            "Mensaje entrante de Twilio procesado. Remitente: {MaskedSender}",
            MaskWaId(waId));

        return Content(twiMlMessageSender.BuildResponse(), "application/xml");
    }

    [HttpPost("status")]
    [Consumes("application/x-www-form-urlencoded")]
    public async Task<IActionResult> Status(CancellationToken cancellationToken)
    {
        var form = await Request.ReadFormAsync(cancellationToken);
        if (!IsValidRequest(form))
        {
            logger.LogWarning("Se rechazó un estado de Twilio con firma inválida");
            return Unauthorized();
        }

        var providerMessageId = form["MessageSid"].ToString();
        var status = form["MessageStatus"].ToString();
        if (!string.IsNullOrWhiteSpace(providerMessageId) && !string.IsNullOrWhiteSpace(status))
        {
            await conversationService.UpdateStatusAsync(
                providerMessageId,
                status,
                DateTimeOffset.UtcNow,
                cancellationToken);
        }

        return Ok();
    }

    private bool IsValidRequest(IFormCollection form)
    {
        var parameters = form.ToDictionary(
            item => item.Key,
            item => item.Value.ToString(),
            StringComparer.Ordinal);
        return signatureValidator.IsValid(
            GetPublicRequestUrl(),
            parameters,
            Request.Headers["X-Twilio-Signature"].FirstOrDefault());
    }

    private string GetPublicRequestUrl()
    {
        var scheme = FirstForwardedValue("X-Forwarded-Proto") ?? Request.Scheme;
        var host = FirstForwardedValue("X-Forwarded-Host") ?? Request.Host.Value;
        return $"{scheme}://{host}{Request.PathBase}{Request.Path}{Request.QueryString}";
    }

    private string? FirstForwardedValue(string headerName)
    {
        var value = Request.Headers[headerName].FirstOrDefault();
        return string.IsNullOrWhiteSpace(value)
            ? null
            : value.Split(',', 2, StringSplitOptions.TrimEntries)[0];
    }

    private static string NormalizeWaId(string waId, string from)
    {
        var value = string.IsNullOrWhiteSpace(waId) ? from : waId;
        if (value.StartsWith("whatsapp:", StringComparison.OrdinalIgnoreCase))
        {
            value = value["whatsapp:".Length..];
        }

        return value.Trim().TrimStart('+');
    }

    private static string MaskWaId(string waId) =>
        waId.Length <= 4 ? "****" : $"***{waId[^4..]}";
}
