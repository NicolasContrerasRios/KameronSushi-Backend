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
                   a.purgado_en
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
                reader.GetBoolean(2), reader.GetBoolean(3),
                reader.IsDBNull(4) ? null : reader.GetFieldValue<DateTimeOffset>(4)));
        }
        return months;
    }

    public async Task<MonthlyDataArchive> BuildArchiveAsync(DateOnly periodStart, CancellationToken cancellationToken)
    {
        ValidatePeriod(periodStart);
        var periodEnd = periodStart.AddMonths(1);
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
            orders.Add(new ArchivedOrder(details, history));
        }

        var messages = new List<ArchivedWhatsAppMessage>();
        await using (var connection = await dataSource.OpenConnectionAsync(cancellationToken))
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT id_mensaje,id_conversacion,id_pedido,id_mensaje_proveedor,direccion,tipo,contenido,estado,creado_en
                  FROM mensajes_whatsapp
                 WHERE (creado_en >= (@start::date::timestamp AT TIME ZONE 'America/Santiago')
                        AND creado_en < (@end::date::timestamp AT TIME ZONE 'America/Santiago'))
                    OR id_pedido IN(
                        SELECT id_pedido FROM pedidos
                         WHERE creado_en >= (@start::date::timestamp AT TIME ZONE 'America/Santiago')
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
                    reader.IsDBNull(6) ? null : reader.GetString(6), reader.GetString(7),
                    reader.GetFieldValue<DateTimeOffset>(8)));
            }
        }

        return new MonthlyDataArchive(1, periodStart, periodEnd, DateTimeOffset.UtcNow, shifts, orders, messages);
    }

    public async Task RegisterPreparedArchiveAsync(
        DateOnly periodStart, string sha256, long sizeBytes, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO archivos_mensuales(periodo_inicio,periodo_fin,sha256,tamano_bytes,preparado_en)
            VALUES(@start,@end,@sha,@size,NOW())
            ON CONFLICT(periodo_inicio) DO UPDATE SET
                sha256=EXCLUDED.sha256,tamano_bytes=EXCLUDED.tamano_bytes,preparado_en=NOW();
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
        if (periodEnd > retentionBoundary)
            throw new ArgumentException("El período todavía está dentro de los tres meses de retención.");

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using (var lockCommand = connection.CreateCommand())
        {
            lockCommand.Transaction = transaction;
            lockCommand.CommandText = "SELECT pg_advisory_xact_lock(hashtextextended(@period,0));";
            lockCommand.Parameters.AddWithValue("period", $"archive:{periodStart:yyyy-MM}");
            await lockCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var validate = connection.CreateCommand())
        {
            validate.Transaction = transaction;
            validate.CommandText = """
                SELECT EXISTS(
                    SELECT 1 FROM archivos_mensuales
                     WHERE periodo_inicio=@start AND sha256=@sha
                       AND preparado_en >= NOW()-INTERVAL '1 hour' AND purgado_en IS NULL),
                       EXISTS(SELECT 1 FROM pedidos
                               WHERE creado_en >= (@start::date::timestamp AT TIME ZONE 'America/Santiago')
                                 AND creado_en < (@end::date::timestamp AT TIME ZONE 'America/Santiago')
                                 AND estado NOT IN('listo','cancelado')),
                       EXISTS(SELECT 1 FROM turnos
                               WHERE abierto_en < (@end::date::timestamp AT TIME ZONE 'America/Santiago')
                                 AND (cerrado_en IS NULL OR estado<>'cerrado'));
                """;
            AddPeriod(validate, periodStart, periodEnd);
            validate.Parameters.AddWithValue("sha", sha256);
            await using var reader = await validate.ExecuteReaderAsync(cancellationToken);
            await reader.ReadAsync(cancellationToken);
            if (!reader.GetBoolean(0)) throw new ArgumentException("El archivo no coincide o su preparación expiró. Descárgalo nuevamente.");
            if (reader.GetBoolean(1) || reader.GetBoolean(2)) throw new ArgumentException("El período contiene pedidos o turnos todavía activos.");
        }

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
            DELETE FROM mensajes_whatsapp WHERE id_pedido IN(
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
        var deletedOrders = await ExecuteAsync(connection, transaction, """
            DELETE FROM pedidos
             WHERE creado_en >= (@start::date::timestamp AT TIME ZONE 'America/Santiago')
               AND creado_en < (@end::date::timestamp AT TIME ZONE 'America/Santiago');
            """, periodStart, periodEnd, cancellationToken);

        var deletedMessages = await ExecuteAsync(connection, transaction,
            "DELETE FROM mensajes_whatsapp WHERE creado_en >= (@start::date::timestamp AT TIME ZONE 'America/Santiago') AND creado_en < (@end::date::timestamp AT TIME ZONE 'America/Santiago');",
            periodStart, periodEnd, cancellationToken);
        await ExecuteAsync(connection, transaction, """
            DELETE FROM movimientos_caja WHERE id_turno IN(
                SELECT t.id_turno FROM turnos t
                 WHERE t.cerrado_en >= (@start::date::timestamp AT TIME ZONE 'America/Santiago')
                   AND t.cerrado_en < (@end::date::timestamp AT TIME ZONE 'America/Santiago') AND t.estado='cerrado'
                   AND NOT EXISTS(SELECT 1 FROM pedidos p WHERE p.id_turno=t.id_turno));
            """, periodStart, periodEnd, cancellationToken);
        var deletedShifts = await ExecuteAsync(connection, transaction,
            """
            DELETE FROM turnos t
             WHERE t.cerrado_en >= (@start::date::timestamp AT TIME ZONE 'America/Santiago')
               AND t.cerrado_en < (@end::date::timestamp AT TIME ZONE 'America/Santiago') AND t.estado='cerrado'
               AND NOT EXISTS(SELECT 1 FROM pedidos p WHERE p.id_turno=t.id_turno);
            """,
            periodStart, periodEnd, cancellationToken);

        var purgedAt = DateTimeOffset.UtcNow;
        await using (var mark = connection.CreateCommand())
        {
            mark.Transaction = transaction;
            mark.CommandText = """
                UPDATE archivos_mensuales SET confirmado_en=NOW(),confirmado_por=@user,purgado_en=NOW()
                 WHERE periodo_inicio=@start;
                """;
            mark.Parameters.AddWithValue("user", userId);
            mark.Parameters.AddWithValue("start", periodStart);
            await mark.ExecuteNonQueryAsync(cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
        return new ArchivePurgeResult(periodStart, deletedOrders, deletedShifts, deletedMessages, purgedAt);
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
