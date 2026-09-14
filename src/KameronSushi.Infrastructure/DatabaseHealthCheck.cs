using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;

namespace KameronSushi.Infrastructure;

public sealed class DatabaseHealthCheck(NpgsqlDataSource dataSource) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var command = dataSource.CreateCommand("SELECT 1");
            await command.ExecuteScalarAsync(cancellationToken);
            return HealthCheckResult.Healthy("PostgreSQL disponible");
        }
        catch (Exception exception)
        {
            return HealthCheckResult.Unhealthy("No fue posible conectar con PostgreSQL", exception);
        }
    }
}
