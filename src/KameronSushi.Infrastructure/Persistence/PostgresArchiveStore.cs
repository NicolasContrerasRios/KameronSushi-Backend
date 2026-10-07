using KameronSushi.Application.Abstractions;
using KameronSushi.Application.Admin;
using KameronSushi.Application.Archives;
using Npgsql;
using NpgsqlTypes;

namespace KameronSushi.Infrastructure.Persistence;

public sealed class PostgresArchiveStore(NpgsqlDataSource dataSource) : IArchiveStore
{
    public async Task<IReadOnlyList<ArchiveMonth>> GetMonthsAsync(CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            WITH meses AS (
                SELECT DISTINCT date_trunc('month', fecha AT TIME ZONE 'America/Santiago')::date AS inicio
                  FROM (
                    SELECT creado_en AS fecha FROM pedidos
                    UNION ALL SELECT cerrado_en FROM turnos WHERE cerrado_en IS NOT NULL
                    UNION ALL SELECT creado_en FROM mensajes_whatsapp
                  ) datos
                 WHERE fecha < (date_trunc('month', NOW() AT TIME ZONE 'America/Santiago') AT TIME ZONE 'America/Santiago')
            )
            SELECT m.inicio, (m.inicio + INTERVAL '1 month')::date,
                   (m.inicio + INTERVAL '1 month') <= ((NOW() AT TIME ZONE 'America/Santiago')::date - 60),
                   (m.inicio + INTERVAL '1 month') <= (date_trunc('month', NOW() AT TIME ZONE 'America/Santiago') - INTERVAL '3 months')::date,
                   NOT EXISTS (
                       SELECT 1 FROM pedidos p
                        WHERE p.creado_en >= (m.inicio::timestamp AT TIME ZONE 'America/Santiago')
                          AND p.creado_en < ((m.inicio + INTERVAL '1 month')::timestamp AT TIME ZONE 'America/Santiago')
                          AND p.estado NOT IN ('listo','cancelado'))
                   AND NOT EXISTS (
                       SELECT 1 FROM turnos t
                        WHERE t.abierto_en < ((m.inicio + INTERVAL '1 month')::timestamp AT TIME ZONE 'America/Santiago')
                          AND (t.cerrado_en IS NULL OR t.estado <> 'cerrado')),
                   a.confirmado_en, a.sha256, a.mensajes_purgados_en, a.purgado_en
              FROM meses m
              LEFT JOIN archivos_mensuales a ON a.periodo_inicio = m.inicio
             ORDER BY m.inicio;
            """;
        var months = new List<ArchiveMonth>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            months.Add(new ArchiveMonth(
                reader.GetFieldValue<DateOnly>(0), reader.GetFieldValue<DateOnly>(1),
                reader.GetBoolean(2), reader.GetBoolean(3), reader.GetBoolean(4),
                reader.IsDBNull(5) ? null : reader.GetFieldValue<DateTimeOffset>(5),
                reader.IsDBNull(6) ? null : reader.GetString(6),
                reader.IsDBNull(7) ? null : reader.GetFieldValue<DateTimeOffset>(7),
                reader.IsDBNull(8) ? null : reader.GetFieldValue<DateTimeOffset>(8)));
        }
        return months;
    }

    public async Task<MonthlyDataArchive> BuildArchiveAsync(DateOnly periodStart, CancellationToken cancellationToken)
    {
        ValidatePeriod(periodStart);
        var periodEnd = periodStart.AddMonths(1);
        await EnsureArchiveCanBeBuiltAsync(periodStart, cancellationToken);
        var posStore = new PostgresPosStore(dataSource);
        var shifts = new List<ArchivedShift>();
        var orders = new List<ArchivedOrder>();

        await using (var connection = await dataSource.OpenConnectionAsync(cancellationToken))
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT t.id_turno, t.estado, t.abierto_en, t.cerrado_en, u.nombre,
                       (SELECT COUNT(*)::int FROM pedidos p WHERE p.id_turno=t.id_turno AND p.estado<>'cancelado'),
                       (SELECT COALESCE(SUM(p.total),0) FROM pedidos p WHERE p.id_turno=t.id_turno AND p.estado<>'cancelado'),
                       t.diferencia_caja
                  FROM turnos t JOIN usuarios u ON u.id_usuario=t.id_usuario_apertura
                 WHERE t.cerrado_en >= (@start::date::timestamp AT TIME ZONE 'America/Santiago')
                   AND t.cerrado_en < (@end::date::timestamp AT TIME ZONE 'America/Santiago')
                 ORDER BY t.cerrado_en, t.id_turno;
                """;
            AddPeriod(command, periodStart, periodEnd);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var summary = new AdminShiftSummary(
                    reader.GetInt64(0), reader.GetString(1), reader.GetFieldValue<DateTimeOffset>(2),
                    reader.IsDBNull(3) ? null : reader.GetFieldValue<DateTimeOffset>(3), reader.GetString(4),
                    reader.GetInt32(5), reader.GetDecimal(6), reader.IsDBNull(7) ? null : reader.GetDecimal(7));
                var report = await posStore.GetShiftReportAsync(summary.ShiftId, cancellationToken)
                    ?? throw new InvalidOperationException($"No se pudo construir el turno {summary.ShiftId}.");
                shifts.Add(new ArchivedShift(summary, report));
            }
        }

        var orderIds = new List<long>();
        await using (var connection = await dataSource.OpenConnectionAsync(cancellationToken))
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT id_pedido FROM pedidos
                 WHERE creado_en >= (@start::date::timestamp AT TIME ZONE 'America/Santiago')
                   AND creado_en < (@end::date::timestamp AT TIME ZONE 'America/Santiago')
                 ORDER BY creado_en, id_pedido;
                """;
            AddPeriod(command, periodStart, periodEnd);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken)) orderIds.Add(reader.GetInt64(0));
        }
        foreach (var orderId in orderIds)
        {
            var details = await posStore.GetOrderAsync(orderId, cancellationToken)
                ?? throw new InvalidOperationException($"No se pudo construir el pedido {orderId}.");
            var history = await posStore.GetOrderHistoryAsync(orderId, cancellationToken);
            var extras = await LoadOrderExtrasAsync(orderId, cancellationToken);
            orders.Add(new ArchivedOrder(
                details, history, extras.Delivery, extras.RollConfigurations,
                extras.Checkouts, extras.PaymentEvents));
        }

        var messages = new List<ArchivedWhatsAppMessage>();
        await using (var connection = await dataSource.OpenConnectionAsync(cancellationToken))
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT id_mensaje,id_conversacion,id_pedido,id_mensaje_proveedor,direccion,tipo,
                       contenido,nombre_plantilla,estado,opcion_seleccionada,detalle_error,
                       enviado_en,entregado_en,leido_en,creado_en
                  FROM mensajes_whatsapp
                 WHERE (creado_en >= (@start::date::timestamp AT TIME ZONE 'America/Santiago')
                        AND creado_en < (@end::date::timestamp AT TIME ZONE 'America/Santiago'))
                 ORDER BY creado_en,id_mensaje;
                """;
            AddPeriod(command, periodStart, periodEnd);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                messages.Add(new ArchivedWhatsAppMessage(
                    reader.GetInt64(0), reader.GetInt64(1), reader.IsDBNull(2) ? null : reader.GetInt64(2),
                    reader.IsDBNull(3) ? null : reader.GetString(3), reader.GetString(4), reader.GetString(5),
                    reader.IsDBNull(6) ? null : reader.GetString(6),
                    reader.IsDBNull(7) ? null : reader.GetString(7), reader.GetString(8),
                    reader.IsDBNull(9) ? null : reader.GetString(9),
                    reader.IsDBNull(10) ? null : reader.GetString(10),
                    reader.IsDBNull(11) ? null : reader.GetFieldValue<DateTimeOffset>(11),
                    reader.IsDBNull(12) ? null : reader.GetFieldValue<DateTimeOffset>(12),
                    reader.IsDBNull(13) ? null : reader.GetFieldValue<DateTimeOffset>(13),
                    reader.GetFieldValue<DateTimeOffset>(14)));
            }
        }

        return new MonthlyDataArchive(2, periodStart, periodEnd, DateTimeOffset.UtcNow, shifts, orders, messages);
    }

    public async Task RegisterPreparedArchiveAsync(
        DateOnly periodStart, string sha256, long sizeBytes, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO archivos_mensuales(periodo_inicio,periodo_fin,sha256,tamano_bytes,preparado_en,version_esquema)
            VALUES(@start,@end,@sha,@size,NOW(),2)
            ON CONFLICT(periodo_inicio) DO UPDATE SET
                sha256=EXCLUDED.sha256,tamano_bytes=EXCLUDED.tamano_bytes,preparado_en=NOW(),version_esquema=2
             WHERE archivos_mensuales.confirmado_en IS NULL;
            """;
        command.Parameters.AddWithValue("start", periodStart);
        command.Parameters.AddWithValue("end", periodStart.AddMonths(1));
        command.Parameters.AddWithValue("sha", sha256);
        command.Parameters.AddWithValue("size", sizeBytes);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<ArchivePurgeResult> ConfirmAndPurgeAsync(
        DateOnly periodStart, string sha256, long userId, CancellationToken cancellationToken)
    {
        ValidatePeriod(periodStart);
        var periodEnd = periodStart.AddMonths(1);
        var localNow = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, ChileTimeZone);
        var retentionBoundary = new DateOnly(localNow.Year, localNow.Month, 1).AddMonths(-3);
        var localDate = DateOnly.FromDateTime(localNow.DateTime);
        var messagesEligible = periodEnd <= localDate.AddDays(-60);
        var fullPurgeEligible = periodEnd <= retentionBoundary;
        if (!messagesEligible && !fullPurgeEligible)
            throw new ArgumentException("El período todavía está dentro de la retención configurada.");

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using (var lockCommand = connection.CreateCommand())
        {
            lockCommand.Transaction = transaction;
            lockCommand.CommandText = "SELECT pg_advisory_xact_lock(hashtextextended(@period,0));";
            lockCommand.Parameters.AddWithValue("period", $"archive:{periodStart:yyyy-MM}");
            await lockCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        DateTimeOffset? existingConfirmation;
        DateTimeOffset? existingMessagePurge;
        await using (var validate = connection.CreateCommand())
        {
            validate.Transaction = transaction;
            validate.CommandText = """
                SELECT confirmado_en,mensajes_purgados_en,purgado_en,preparado_en
                  FROM archivos_mensuales
                 WHERE periodo_inicio=@start AND sha256=@sha
                 FOR UPDATE;
                """;
            validate.Parameters.AddWithValue("start", NpgsqlDbType.Date, periodStart);
            validate.Parameters.AddWithValue("sha", sha256);
            await using var reader = await validate.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
                throw new ArgumentException("El archivo no coincide con el preparado por el servidor.");
            existingConfirmation = reader.IsDBNull(0) ? null : reader.GetFieldValue<DateTimeOffset>(0);
            existingMessagePurge = reader.IsDBNull(1) ? null : reader.GetFieldValue<DateTimeOffset>(1);
            if (!reader.IsDBNull(2)) throw new ArgumentException("El período ya fue depurado.");
            var preparedAt = reader.GetFieldValue<DateTimeOffset>(3);
            if (existingConfirmation is null && preparedAt < DateTimeOffset.UtcNow.AddHours(-1))
                throw new ArgumentException("La preparación expiró. Descarga nuevamente el archivo antes de confirmarlo.");
        }

        await EnsurePeriodIsClosedAsync(connection, transaction, periodStart, periodEnd, cancellationToken);

        var deletedMessages = 0;
        DateTimeOffset? messagesPurgedAt = existingMessagePurge;
        if (messagesEligible && existingMessagePurge is null)
        {
            deletedMessages = await ExecuteAsync(connection, transaction,
                "DELETE FROM mensajes_whatsapp WHERE creado_en >= (@start::date::timestamp AT TIME ZONE 'America/Santiago') AND creado_en < (@end::date::timestamp AT TIME ZONE 'America/Santiago');",
                periodStart, periodEnd, cancellationToken);
            messagesPurgedAt = DateTimeOffset.UtcNow;
        }

        var deletedOrders = 0;
        var deletedShifts = 0;
        DateTimeOffset? purgedAt = null;
        if (fullPurgeEligible)
        {
            await ExecuteFullPurgePreparationAsync(connection, transaction, periodStart, periodEnd, cancellationToken);
            deletedOrders = await ExecuteAsync(connection, transaction, """
                DELETE FROM pedidos
                 WHERE creado_en >= (@start::date::timestamp AT TIME ZONE 'America/Santiago')
                   AND creado_en < (@end::date::timestamp AT TIME ZONE 'America/Santiago');
                """, periodStart, periodEnd, cancellationToken);
            await ExecuteAsync(connection, transaction, """
                DELETE FROM movimientos_caja WHERE id_turno IN(
                    SELECT t.id_turno FROM turnos t
                     WHERE t.cerrado_en >= (@start::date::timestamp AT TIME ZONE 'America/Santiago')
                       AND t.cerrado_en < (@end::date::timestamp AT TIME ZONE 'America/Santiago') AND t.estado='cerrado'
                       AND NOT EXISTS(SELECT 1 FROM pedidos p WHERE p.id_turno=t.id_turno));
                """, periodStart, periodEnd, cancellationToken);
            deletedShifts = await ExecuteAsync(connection, transaction, """
                DELETE FROM turnos t
                 WHERE t.cerrado_en >= (@start::date::timestamp AT TIME ZONE 'America/Santiago')
                   AND t.cerrado_en < (@end::date::timestamp AT TIME ZONE 'America/Santiago') AND t.estado='cerrado'
                   AND NOT EXISTS(SELECT 1 FROM pedidos p WHERE p.id_turno=t.id_turno);
                """, periodStart, periodEnd, cancellationToken);
            purgedAt = DateTimeOffset.UtcNow;
        }

        await using (var mark = connection.CreateCommand())
        {
            mark.Transaction = transaction;
            mark.CommandText = """
                UPDATE archivos_mensuales
                   SET confirmado_en=COALESCE(confirmado_en,NOW()),
                       confirmado_por=COALESCE(confirmado_por,@user),
                       mensajes_purgados_en=CASE WHEN @clean_messages THEN COALESCE(mensajes_purgados_en,NOW()) ELSE mensajes_purgados_en END,
                       purgado_en=CASE WHEN @full_purge THEN COALESCE(purgado_en,NOW()) ELSE purgado_en END
                 WHERE periodo_inicio=@start;
                """;
            mark.Parameters.AddWithValue("user", userId);
            mark.Parameters.AddWithValue("start", periodStart);
            mark.Parameters.AddWithValue("clean_messages", messagesEligible);
            mark.Parameters.AddWithValue("full_purge", fullPurgeEligible);
            await mark.ExecuteNonQueryAsync(cancellationToken);
        }
        await ExecuteMaintenanceAsync(connection, transaction, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new ArchivePurgeResult(
            periodStart, deletedOrders, deletedShifts, deletedMessages,
            messagesPurgedAt, purgedAt);
    }

    private static async Task ExecuteMaintenanceAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            DELETE FROM sesiones_usuario
             WHERE expira_en < NOW()-INTERVAL '30 days'
                OR revocada_en < NOW()-INTERVAL '30 days';
            DELETE FROM eventos_pagos
             WHERE id_pago IS NULL
               AND recibido_en < NOW()-INTERVAL '90 days'
               AND estado_procesamiento='procesado';
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task EnsurePeriodIsClosedAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction,
        DateOnly periodStart, DateOnly periodEnd, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT EXISTS(SELECT 1 FROM pedidos
                           WHERE creado_en >= (@start::date::timestamp AT TIME ZONE 'America/Santiago')
                             AND creado_en < (@end::date::timestamp AT TIME ZONE 'America/Santiago')
                             AND estado NOT IN('listo','cancelado')),
                   EXISTS(SELECT 1 FROM turnos
                           WHERE abierto_en < (@end::date::timestamp AT TIME ZONE 'America/Santiago')
                             AND (cerrado_en IS NULL OR estado<>'cerrado'));
            """;
        AddPeriod(command, periodStart, periodEnd);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        if (reader.GetBoolean(0) || reader.GetBoolean(1))
            throw new ArgumentException("El período contiene pedidos o turnos todavía activos.");
    }

    private static async Task ExecuteFullPurgePreparationAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction,
        DateOnly periodStart, DateOnly periodEnd, CancellationToken cancellationToken)
    {
        await ExecuteAsync(connection, transaction, """
            DELETE FROM eventos_pagos WHERE id_pago IN(
                SELECT pa.id_pago FROM pagos pa JOIN pedidos pe ON pe.id_pedido=pa.id_pedido
                 WHERE pe.creado_en >= (@start::date::timestamp AT TIME ZONE 'America/Santiago')
                   AND pe.creado_en < (@end::date::timestamp AT TIME ZONE 'America/Santiago'));
            """, periodStart, periodEnd, cancellationToken);
        await ExecuteAsync(connection, transaction, """
            UPDATE movimiento_puntos SET id_pedido=NULL WHERE id_pedido IN(
                SELECT id_pedido FROM pedidos WHERE creado_en >= (@start::date::timestamp AT TIME ZONE 'America/Santiago') AND creado_en < (@end::date::timestamp AT TIME ZONE 'America/Santiago'));
            """, periodStart, periodEnd, cancellationToken);
        await ExecuteAsync(connection, transaction, """
            UPDATE mensajes_whatsapp SET id_pedido=NULL WHERE id_pedido IN(
                SELECT id_pedido FROM pedidos WHERE creado_en >= (@start::date::timestamp AT TIME ZONE 'America/Santiago') AND creado_en < (@end::date::timestamp AT TIME ZONE 'America/Santiago'));
            """, periodStart, periodEnd, cancellationToken);
        await ExecuteAsync(connection, transaction, """
            DELETE FROM historial_estados_pedido WHERE id_pedido IN(
                SELECT id_pedido FROM pedidos WHERE creado_en >= (@start::date::timestamp AT TIME ZONE 'America/Santiago') AND creado_en < (@end::date::timestamp AT TIME ZONE 'America/Santiago'));
            """, periodStart, periodEnd, cancellationToken);
        await ExecuteAsync(connection, transaction, """
            DELETE FROM pagos WHERE id_pedido IN(
                SELECT id_pedido FROM pedidos WHERE creado_en >= (@start::date::timestamp AT TIME ZONE 'America/Santiago') AND creado_en < (@end::date::timestamp AT TIME ZONE 'America/Santiago'));
            """, periodStart, periodEnd, cancellationToken);
    }

    private async Task EnsureArchiveCanBeBuiltAsync(DateOnly periodStart, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand("""
            SELECT mensajes_purgados_en IS NOT NULL
              FROM archivos_mensuales
             WHERE periodo_inicio=@start;
            """);
        command.Parameters.AddWithValue("start", NpgsqlDbType.Date, periodStart);
        var result = await command.ExecuteScalarAsync(cancellationToken);
        if (result is true)
            throw new ArgumentException("Los mensajes de este período ya fueron depurados. Usa la copia local verificada.");
    }

    private async Task<OrderArchiveExtras> LoadOrderExtrasAsync(long orderId, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        ArchivedDelivery? delivery = null;
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT nombre_receptor,telefono_receptor,calle,numero,departamento,comuna,referencia,
                       estado,despachado_en,entregado_en,creado_en,actualizado_en
                  FROM entregas_pedido WHERE id_pedido=@order;
                """;
            command.Parameters.AddWithValue("order", orderId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
                delivery = new ArchivedDelivery(
                    reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
                    reader.IsDBNull(4) ? null : reader.GetString(4), reader.GetString(5),
                    reader.IsDBNull(6) ? null : reader.GetString(6), reader.GetString(7),
                    reader.IsDBNull(8) ? null : reader.GetFieldValue<DateTimeOffset>(8),
                    reader.IsDBNull(9) ? null : reader.GetFieldValue<DateTimeOffset>(9),
                    reader.GetFieldValue<DateTimeOffset>(10), reader.GetFieldValue<DateTimeOffset>(11));
        }

        var configurations = new List<ArchivedRollConfiguration>();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT c.id_detalle,c.configuracion_snapshot::text
                  FROM configuracion_roll_pedido c
                  JOIN detalle_pedidos d ON d.id_detalle=c.id_detalle
                 WHERE d.id_pedido=@order ORDER BY c.id_detalle;
                """;
            command.Parameters.AddWithValue("order", orderId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
                configurations.Add(new ArchivedRollConfiguration(reader.GetInt64(0), reader.GetString(1)));
        }

        var checkouts = new List<ArchivedCheckout>();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT id_checkout,id_preferencia,referencia_comercio,url_pago,monto,moneda,creado_en,vence_en
                  FROM checkouts WHERE id_pedido=@order ORDER BY id_checkout;
                """;
            command.Parameters.AddWithValue("order", orderId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
                checkouts.Add(new ArchivedCheckout(
                    reader.GetInt64(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
                    reader.GetDecimal(4), reader.GetString(5), reader.GetFieldValue<DateTimeOffset>(6),
                    reader.IsDBNull(7) ? null : reader.GetFieldValue<DateTimeOffset>(7)));
        }

        var events = new List<ArchivedPaymentEvent>();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT e.id_evento,e.id_pago,e.proveedor,e.id_evento_proveedor,e.tipo,e.id_recurso_proveedor,
                       e.estado_procesamiento,e.intentos,e.ultimo_error,e.recibido_en,e.procesado_en
                  FROM eventos_pagos e
                  JOIN pagos p ON p.id_pago=e.id_pago
                 WHERE p.id_pedido=@order ORDER BY e.recibido_en,e.id_evento;
                """;
            command.Parameters.AddWithValue("order", orderId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
                events.Add(new ArchivedPaymentEvent(
                    reader.GetInt64(0), reader.IsDBNull(1) ? null : reader.GetInt64(1), reader.GetString(2),
                    reader.GetString(3), reader.GetString(4), reader.GetString(5), reader.GetString(6),
                    reader.GetInt32(7), reader.IsDBNull(8) ? null : reader.GetString(8),
                    reader.GetFieldValue<DateTimeOffset>(9),
                    reader.IsDBNull(10) ? null : reader.GetFieldValue<DateTimeOffset>(10)));
        }
        return new OrderArchiveExtras(delivery, configurations, checkouts, events);
    }

    private static void ValidatePeriod(DateOnly periodStart)
    {
        if (periodStart.Day != 1) throw new ArgumentException("El período debe comenzar el primer día del mes.");
        var localNow = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, ChileTimeZone);
        var currentMonth = new DateOnly(localNow.Year, localNow.Month, 1);
        if (periodStart >= currentMonth) throw new ArgumentException("Solo se pueden archivar meses completos.");
    }

    private static readonly TimeZoneInfo ChileTimeZone =
        TimeZoneInfo.FindSystemTimeZoneById("America/Santiago");

    private sealed record OrderArchiveExtras(
        ArchivedDelivery? Delivery,
        IReadOnlyList<ArchivedRollConfiguration> RollConfigurations,
        IReadOnlyList<ArchivedCheckout> Checkouts,
        IReadOnlyList<ArchivedPaymentEvent> PaymentEvents);

    private static void AddPeriod(NpgsqlCommand command, DateOnly start, DateOnly end)
    {
        command.Parameters.AddWithValue("start", NpgsqlDbType.Date, start);
        command.Parameters.AddWithValue("end", NpgsqlDbType.Date, end);
    }

    private static async Task<int> ExecuteAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, string sql,
        DateOnly start, DateOnly end, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        AddPeriod(command, start, end);
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
