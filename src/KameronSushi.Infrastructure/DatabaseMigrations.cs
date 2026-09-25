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

        if (!await IsAppliedAsync(connection, transaction, "003_cash_shift_accounting", cancellationToken))
        {
            await using var migrationCommand = connection.CreateCommand();
            migrationCommand.Transaction = transaction;
            migrationCommand.CommandText = """
                ALTER TABLE turnos ADD COLUMN IF NOT EXISTS monto_inicial NUMERIC(12,0) NOT NULL DEFAULT 0;
                ALTER TABLE turnos ADD COLUMN IF NOT EXISTS efectivo_contado NUMERIC(12,0);
                ALTER TABLE turnos ADD COLUMN IF NOT EXISTS efectivo_esperado NUMERIC(12,0);
                ALTER TABLE turnos ADD COLUMN IF NOT EXISTS diferencia_efectivo NUMERIC(12,0);

                CREATE TABLE IF NOT EXISTS movimientos_caja (
                    id_movimiento BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
                    id_turno BIGINT NOT NULL REFERENCES turnos(id_turno) ON DELETE RESTRICT,
                    tipo VARCHAR(20) NOT NULL,
                    monto NUMERIC(12,0) NOT NULL,
                    motivo VARCHAR(250) NOT NULL,
                    creado_en TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                    CONSTRAINT ck_movimiento_caja_tipo CHECK (tipo IN ('ingreso', 'retiro', 'gasto')),
                    CONSTRAINT ck_movimiento_caja_monto CHECK (monto > 0),
                    CONSTRAINT ck_movimiento_caja_motivo CHECK (btrim(motivo) <> '')
                );
                CREATE INDEX IF NOT EXISTS ix_movimientos_caja_turno
                    ON movimientos_caja (id_turno, creado_en);
                INSERT INTO schema_migrations (id) VALUES ('003_cash_shift_accounting');
                """;
            await migrationCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        if (!await IsAppliedAsync(connection, transaction, "004_admin_catalog", cancellationToken))
        {
            await using var migrationCommand = connection.CreateCommand();
            migrationCommand.Transaction = transaction;
            migrationCommand.CommandText = """
                ALTER TABLE productos ADD COLUMN IF NOT EXISTS orden INTEGER NOT NULL DEFAULT 0;
                ALTER TABLE producto_envolturas ADD COLUMN IF NOT EXISTS activa BOOLEAN NOT NULL DEFAULT TRUE;
                INSERT INTO schema_migrations (id) VALUES ('004_admin_catalog');
                """;
            await migrationCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        if (!await IsAppliedAsync(connection, transaction, "005_promotions", cancellationToken))
        {
            await using var migrationCommand = connection.CreateCommand(); migrationCommand.Transaction = transaction;
            migrationCommand.CommandText = """
                CREATE TABLE IF NOT EXISTS promociones_admin (
                    id_promocion BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
                    id_producto BIGINT NOT NULL UNIQUE REFERENCES productos(id_producto) ON DELETE RESTRICT,
                    productos_incluidos TEXT,
                    salsas_incluidas INTEGER NOT NULL DEFAULT 0 CHECK (salsas_incluidas >= 0),
                    vigente_desde TIMESTAMPTZ,
                    vigente_hasta TIMESTAMPTZ,
                    habilitada_retiro BOOLEAN NOT NULL DEFAULT TRUE,
                    habilitada_delivery BOOLEAN NOT NULL DEFAULT TRUE,
                    activa BOOLEAN NOT NULL DEFAULT TRUE,
                    actualizado_en TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                    CONSTRAINT ck_promocion_vigencia CHECK (vigente_hasta IS NULL OR vigente_desde IS NULL OR vigente_hasta > vigente_desde),
                    CONSTRAINT ck_promocion_canal CHECK (habilitada_retiro OR habilitada_delivery)
                );
                INSERT INTO schema_migrations (id) VALUES ('005_promotions');
                """;
            await migrationCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        if (!await IsAppliedAsync(connection, transaction, "006_admin_kitchen_settings", cancellationToken))
        {
            await using var migrationCommand = connection.CreateCommand(); migrationCommand.Transaction = transaction; migrationCommand.CommandText = """
                CREATE TABLE IF NOT EXISTS configuracion_operativa (
                    clave VARCHAR(100) PRIMARY KEY,
                    valor VARCHAR(500) NOT NULL,
                    actualizado_en TIMESTAMPTZ NOT NULL DEFAULT NOW()
                );
                INSERT INTO configuracion_operativa(clave,valor) VALUES('tiempo_objetivo_cocina_minutos','20') ON CONFLICT(clave) DO NOTHING;
                INSERT INTO schema_migrations(id) VALUES('006_admin_kitchen_settings');
                """; await migrationCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        if (!await IsAppliedAsync(connection, transaction, "007_kitchen_print_queue", cancellationToken))
        {
            await using var migrationCommand = connection.CreateCommand();
            migrationCommand.Transaction = transaction;
            migrationCommand.CommandText = """
                ALTER TABLE pedidos ADD COLUMN IF NOT EXISTS pendiente_impresion BOOLEAN NOT NULL DEFAULT FALSE;
                ALTER TABLE pedidos ADD COLUMN IF NOT EXISTS impreso_en TIMESTAMPTZ;
                ALTER TABLE pedidos ADD COLUMN IF NOT EXISTS impreso_por VARCHAR(150);
                ALTER TABLE pedidos ADD COLUMN IF NOT EXISTS impresion_token UUID;
                ALTER TABLE pedidos ADD COLUMN IF NOT EXISTS impresion_tomada_en TIMESTAMPTZ;
                ALTER TABLE pedidos ADD COLUMN IF NOT EXISTS impresion_tomada_por VARCHAR(150);
                ALTER TABLE pedidos ADD COLUMN IF NOT EXISTS impresion_reintentar_en TIMESTAMPTZ NOT NULL DEFAULT NOW();
                ALTER TABLE pedidos ADD COLUMN IF NOT EXISTS intentos_impresion INTEGER NOT NULL DEFAULT 0;
                ALTER TABLE pedidos ADD COLUMN IF NOT EXISTS ultimo_error_impresion VARCHAR(500);

                DO $$
                BEGIN
                    IF NOT EXISTS (
                        SELECT 1 FROM pg_constraint
                         WHERE conname = 'ck_pedido_intentos_impresion'
                           AND conrelid = 'pedidos'::regclass
                    ) THEN
                        ALTER TABLE pedidos ADD CONSTRAINT ck_pedido_intentos_impresion
                            CHECK (intentos_impresion >= 0);
                    END IF;
                END;
                $$;

                CREATE INDEX IF NOT EXISTS ix_pedidos_impresion_pendiente
                    ON pedidos (impresion_reintentar_en, creado_en)
                    WHERE pendiente_impresion = TRUE AND impreso_en IS NULL;

                CREATE OR REPLACE FUNCTION encolar_impresion_pedido()
                RETURNS TRIGGER
                LANGUAGE plpgsql
                AS $$
                BEGIN
                    IF NEW.estado = 'cancelado' THEN
                        NEW.pendiente_impresion := FALSE;
                        NEW.impresion_token := NULL;
                        NEW.impresion_tomada_en := NULL;
                        NEW.impresion_tomada_por := NULL;
                    ELSIF TG_OP = 'INSERT' THEN
                        IF NEW.estado IN ('confirmado', 'en_preparacion') AND NEW.impreso_en IS NULL THEN
                            NEW.pendiente_impresion := TRUE;
                            NEW.impresion_reintentar_en := NOW();
                        END IF;
                    ELSIF NEW.estado IN ('confirmado', 'en_preparacion')
                          AND OLD.estado IS DISTINCT FROM NEW.estado
                          AND NEW.impreso_en IS NULL THEN
                        NEW.pendiente_impresion := TRUE;
                        NEW.impresion_reintentar_en := NOW();
                    END IF;
                    RETURN NEW;
                END;
                $$;

                DROP TRIGGER IF EXISTS trg_pedidos_encolar_impresion ON pedidos;
                CREATE TRIGGER trg_pedidos_encolar_impresion
                    BEFORE INSERT OR UPDATE OF estado ON pedidos
                    FOR EACH ROW EXECUTE FUNCTION encolar_impresion_pedido();

                INSERT INTO schema_migrations(id) VALUES('007_kitchen_print_queue');
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
