using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace KameronSushi.Infrastructure;

public static class DatabaseMigrations
{
    public static async Task ApplyKameronSushiMigrationsAsync(
        this IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        var dataSource = services.GetRequiredService<NpgsqlDataSource>();
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        await using (var setupCommand = connection.CreateCommand())
        {
            setupCommand.Transaction = transaction;
            setupCommand.CommandText = """
                SELECT pg_advisory_xact_lock(hashtext('kameron_sushi_schema_migrations'));
                CREATE TABLE IF NOT EXISTS schema_migrations (
                    id VARCHAR(100) PRIMARY KEY,
                    aplicado_en TIMESTAMPTZ NOT NULL DEFAULT NOW()
                );
                """;
            await setupCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        if (!await IsAppliedAsync(connection, transaction, "002_edenred_payment_method", cancellationToken))
        {
            await using var migrationCommand = connection.CreateCommand();
            migrationCommand.Transaction = transaction;
            migrationCommand.CommandText = """
                ALTER TABLE pagos DROP CONSTRAINT IF EXISTS ck_pago_metodo;
                ALTER TABLE pagos ADD CONSTRAINT ck_pago_metodo
                    CHECK (metodo IN ('efectivo', 'transferencia', 'tarjeta', 'edenred'));
                INSERT INTO schema_migrations (id) VALUES ('002_edenred_payment_method');
                """;
            await migrationCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    private static async Task<bool> IsAppliedAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string id,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT EXISTS (SELECT 1 FROM schema_migrations WHERE id = @id);";
        command.Parameters.AddWithValue("id", id);
        return Convert.ToBoolean(await command.ExecuteScalarAsync(cancellationToken));
    }
}
