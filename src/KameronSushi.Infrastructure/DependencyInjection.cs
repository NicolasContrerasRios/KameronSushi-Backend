using KameronSushi.Application.Abstractions;
using KameronSushi.Infrastructure.Configuration;
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
        services.AddSingleton<IWebhookSignatureValidator, MetaWebhookSignatureValidator>();
        services.AddHttpClient<IWhatsAppMessageSender, MetaWhatsAppMessageSender>();
        return services;
    }
}
