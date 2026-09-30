namespace KameronSushi.Infrastructure.Configuration;

public sealed class AuthOptions
{
    public const string SectionName = "Auth";
    public string BootstrapKey { get; set; } = string.Empty;
    public int SessionHours { get; set; } = 12;
}

