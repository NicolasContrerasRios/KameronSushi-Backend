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
            var builder = new NpgsqlConnectionStringBuilder
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
}
