using KameronSushi.Application.Abstractions;
using KameronSushi.Infrastructure.Configuration;
using KameronSushi.Infrastructure.Auth;
using KameronSushi.Infrastructure.Persistence;
using KameronSushi.Infrastructure.WhatsApp;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Npgsql;

namespace KameronSushi.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<DatabaseOptions>(configuration.GetSection(DatabaseOptions.SectionName));
        services.Configure<WhatsAppOptions>(configuration.GetSection(WhatsAppOptions.SectionName));
        services.Configure<TwilioOptions>(configuration.GetSection(TwilioOptions.SectionName));
        services.Configure<AuthOptions>(configuration.GetSection(AuthOptions.SectionName));

        services.AddSingleton(provider =>
        {
            var options = provider.GetRequiredService<IOptions<DatabaseOptions>>().Value;
            var databaseUrl = configuration["DATABASE_URL"] ?? options.Url;
            var builder = string.IsNullOrWhiteSpace(databaseUrl)
                ? BuildFromOptions(options)
                : BuildFromUrl(databaseUrl);
            return NpgsqlDataSource.Create(builder.ConnectionString);
        });

        services.AddScoped<IWhatsAppStore, PostgresWhatsAppStore>();
        services.AddScoped<IPosStore, PostgresPosStore>();
        services.AddScoped<IAdminStore, PostgresAdminStore>();
        services.AddScoped<IArchiveStore, PostgresArchiveStore>();
        services.AddScoped<IAuthStore, PostgresAuthStore>();
        services.AddSingleton<IPasswordHasher, Pbkdf2PasswordHasher>();
        services.AddSingleton<IWebhookSignatureValidator, MetaWebhookSignatureValidator>();
        services.AddSingleton<TwilioWebhookSignatureValidator>();
        services.AddScoped<TwilioTwiMlMessageSender>();
        services.AddHttpClient<MetaWhatsAppMessageSender>();
        services.AddHttpClient<TwilioWhatsAppMessageSender>();
        services.AddScoped<IWhatsAppMessageSender>(provider =>
        {
            var settings = provider.GetRequiredService<IOptions<WhatsAppOptions>>().Value;
            return settings.Provider.Equals("Twilio", StringComparison.OrdinalIgnoreCase)
                ? provider.GetRequiredService<TwilioWhatsAppMessageSender>()
                : provider.GetRequiredService<MetaWhatsAppMessageSender>();
        });
        return services;
    }

    private static NpgsqlConnectionStringBuilder BuildFromOptions(DatabaseOptions options) => new()
    {
        Host = options.Host,
        Port = options.Port,
        Database = options.Name,
        Username = options.Username,
        Password = options.Password,
        SslMode = Enum.TryParse<SslMode>(options.SslMode, true, out var sslMode) ? sslMode : SslMode.Require,
        GssEncryptionMode = GssEncryptionMode.Disable,
        ApplicationName = "KameronSushi.Api"
    };

    private static NpgsqlConnectionStringBuilder BuildFromUrl(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || (uri.Scheme != "postgresql" && uri.Scheme != "postgres"))
        {
            throw new InvalidOperationException("DATABASE_URL debe ser una URL postgresql válida.");
        }

        var credentials = uri.UserInfo.Split(':', 2);
        if (credentials.Length != 2 || string.IsNullOrWhiteSpace(credentials[0]))
        {
            throw new InvalidOperationException("DATABASE_URL debe incluir usuario y contraseña.");
        }

        var database = Uri.UnescapeDataString(uri.AbsolutePath.Trim('/'));
        if (string.IsNullOrWhiteSpace(database))
            throw new InvalidOperationException("DATABASE_URL debe incluir el nombre de la base de datos.");

        return new NpgsqlConnectionStringBuilder
        {
            Host = uri.Host,
            Port = uri.IsDefaultPort ? 5432 : uri.Port,
            Database = database,
            Username = Uri.UnescapeDataString(credentials[0]),
            Password = Uri.UnescapeDataString(credentials[1]),
            SslMode = SslMode.Require,
            GssEncryptionMode = GssEncryptionMode.Disable,
            ApplicationName = "KameronSushi.Api"
        };
    }
}
