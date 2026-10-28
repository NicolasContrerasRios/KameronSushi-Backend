namespace KameronSushi.Infrastructure.Configuration;

public sealed class FirebaseOptions
{
    public const string SectionName = "Firebase";
    public bool Enabled { get; init; }
    public string ProjectId { get; init; } = string.Empty;
    public string ServiceAccountPath { get; init; } = "/etc/secrets/firebase-service-account.json";
    public string ServiceAccountJsonBase64 { get; init; } = string.Empty;
}
