namespace KameronSushi.Infrastructure.Configuration;

public sealed class TwilioOptions
{
    public const string SectionName = "Twilio";
    public string AccountSid { get; init; } = string.Empty;
    public string AuthToken { get; init; } = string.Empty;
    public string WhatsAppNumber { get; init; } = string.Empty;
    public string ApiBaseUrl { get; init; } = "https://api.twilio.com";
}
