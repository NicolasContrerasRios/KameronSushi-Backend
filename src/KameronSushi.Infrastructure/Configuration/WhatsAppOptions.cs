namespace KameronSushi.Infrastructure.Configuration;

public sealed class WhatsAppOptions
{
    public const string SectionName = "WhatsApp";
    public string ApiVersion { get; init; } = "v26.0";
    public string GraphApiBaseUrl { get; init; } = "https://graph.facebook.com";
    public string PhoneNumberId { get; init; } = string.Empty;
    public string BusinessAccountId { get; init; } = string.Empty;
    public string AccessToken { get; init; } = string.Empty;
    public string VerifyToken { get; init; } = string.Empty;
    public string AppSecret { get; init; } = string.Empty;
}
