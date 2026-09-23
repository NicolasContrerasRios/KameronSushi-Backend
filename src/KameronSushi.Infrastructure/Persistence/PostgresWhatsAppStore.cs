using System.Globalization;
using System.Text.Json;
using KameronSushi.Application.Abstractions;
using KameronSushi.Application.WhatsApp;
using Npgsql;
using NpgsqlTypes;

namespace KameronSushi.Infrastructure.Persistence;

public sealed class PostgresWhatsAppStore(NpgsqlDataSource dataSource) : IWhatsAppStore
{
    public async Task<ConversationRegistration> RegisterInboundAsync(IncomingWhatsAppMessage message, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        const string conversationSql = """
            INSERT INTO conversaciones_whatsapp (wa_id, telefono, ultimo_mensaje_cliente_en)
            VALUES (@wa_id, @telefono, NOW())
            ON CONFLICT (wa_id) DO UPDATE
            SET telefono = EXCLUDED.telefono,
                ultimo_mensaje_cliente_en = EXCLUDED.ultimo_mensaje_cliente_en
            RETURNING id_conversacion, paso_actual, contexto::text;
            """;
        await using var conversationCommand = new NpgsqlCommand(conversationSql, connection, transaction);
        conversationCommand.Parameters.AddWithValue("wa_id", message.WaId);
        conversationCommand.Parameters.AddWithValue("telefono", message.Phone);
        await using var reader = await conversationCommand.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        var conversationId = reader.GetInt64(0);
        var step = reader.GetString(1);
        var contextJson = reader.GetString(2);
        await reader.CloseAsync();

        const string messageSql = """
            INSERT INTO mensajes_whatsapp
                (id_conversacion, id_mensaje_proveedor, direccion, tipo, contenido, estado, opcion_seleccionada)
            VALUES (@conversation_id, @provider_id, 'entrante', @type, @content, 'pendiente', @selection)
            ON CONFLICT (id_mensaje_proveedor) DO NOTHING
            RETURNING id_mensaje;
            """;
        await using var messageCommand = new NpgsqlCommand(messageSql, connection, transaction);
        messageCommand.Parameters.AddWithValue("conversation_id", conversationId);
        messageCommand.Parameters.AddWithValue("provider_id", message.ProviderMessageId);
        messageCommand.Parameters.AddWithValue("type", message.Type);
        messageCommand.Parameters.AddWithValue("content", (object?)message.Text ?? DBNull.Value);
        messageCommand.Parameters.AddWithValue("selection", (object?)message.SelectionId ?? DBNull.Value);
        var insertedId = await messageCommand.ExecuteScalarAsync(cancellationToken);

        var isDuplicate = false;
        if (insertedId is null)
        {
            await using var stateCommand = new NpgsqlCommand(
                "SELECT estado FROM mensajes_whatsapp WHERE id_mensaje_proveedor = @provider_id", connection, transaction);
            stateCommand.Parameters.AddWithValue("provider_id", message.ProviderMessageId);
            var existingState = (string?)await stateCommand.ExecuteScalarAsync(cancellationToken);
            isDuplicate = existingState != "pendiente";
        }

        await transaction.CommitAsync(cancellationToken);
        return new ConversationRegistration(
            conversationId,
            step,
            isDuplicate,
            DeserializeContext(contextJson));
    }

    public async Task MarkInboundProcessedAsync(string providerMessageId, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand("""
            UPDATE mensajes_whatsapp
            SET estado = 'recibido'
            WHERE id_mensaje_proveedor = @provider_id AND direccion = 'entrante';
            """);
        command.Parameters.AddWithValue("provider_id", providerMessageId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task UpdateConversationAsync(long conversationId, string step, IReadOnlyDictionary<string, string?> contextChanges, CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE conversaciones_whatsapp
            SET paso_actual = @step,
                contexto = jsonb_strip_nulls(contexto || @changes::jsonb)
            WHERE id_conversacion = @conversation_id;
            """;
        await using var command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue("step", step);
        command.Parameters.AddWithValue("changes", NpgsqlDbType.Jsonb, JsonSerializer.Serialize(contextChanges));
        command.Parameters.AddWithValue("conversation_id", conversationId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyDictionary<string, string>> GetConversationContextAsync(long conversationId, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            "SELECT contexto::text FROM conversaciones_whatsapp WHERE id_conversacion = @id");
        command.Parameters.AddWithValue("id", conversationId);
        var json = (string?)await command.ExecuteScalarAsync(cancellationToken);
        return DeserializeContext(json ?? "{}");
    }

    public async Task<IReadOnlyList<MenuCategory>> GetCategoriesAsync(CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT id_categoria, nombre_categoria, descripcion_categoria
            FROM categorias
            WHERE activa
            ORDER BY orden, nombre_categoria;
            """;
        await using var command = dataSource.CreateCommand(sql);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var result = new List<MenuCategory>();
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new MenuCategory(reader.GetInt64(0), reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetString(2)));
        }
        return result;
    }

    public async Task<IReadOnlyList<MenuProduct>> GetProductsAsync(long categoryId, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT id_producto, nombre_producto, descripcion, precio, requiere_configuracion
            FROM productos
            WHERE id_categoria = @category_id AND activo AND disponible
            ORDER BY nombre_producto;
            """;
        await using var command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue("category_id", categoryId);
        return await ReadProductsAsync(command, cancellationToken);
    }

    public async Task<MenuProduct?> GetProductAsync(long productId, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT id_producto, nombre_producto, descripcion, precio, requiere_configuracion
            FROM productos
            WHERE id_producto = @product_id AND activo AND disponible;
            """;
        await using var command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue("product_id", productId);
        return (await ReadProductsAsync(command, cancellationToken)).SingleOrDefault();
    }

    public async Task<IReadOnlyList<RollOption>> GetRollOptionsAsync(long productId, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT id_opcion_roll, id_producto, numero_opcion, ingredientes, id_producto_envoltura_fija
            FROM opciones_roll
            WHERE id_producto = @product_id AND activa
            ORDER BY numero_opcion;
            """;
        await using var command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue("product_id", productId);
        return await ReadRollOptionsAsync(command, cancellationToken);
    }

    public async Task<RollOption?> GetRollOptionAsync(long optionId, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT id_opcion_roll, id_producto, numero_opcion, ingredientes, id_producto_envoltura_fija
            FROM opciones_roll
            WHERE id_opcion_roll = @option_id AND activa;
            """;
        await using var command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue("option_id", optionId);
        return (await ReadRollOptionsAsync(command, cancellationToken)).SingleOrDefault();
    }

    public async Task<IReadOnlyList<ProductWrapping>> GetWrappingsAsync(long productId, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT pe.id_producto_envoltura, e.nombre, pe.precio_adicional
            FROM producto_envolturas pe
            JOIN envolturas e ON e.id_envoltura = pe.id_envoltura
            WHERE pe.id_producto = @product_id AND e.activa
              AND pe.activa = TRUE
            ORDER BY pe.predeterminada DESC, pe.precio_adicional, e.nombre;
            """;
        await using var command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue("product_id", productId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var result = new List<ProductWrapping>();
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new ProductWrapping(reader.GetInt64(0), reader.GetString(1), reader.GetDecimal(2)));
        }
        return result;
    }

    public async Task<IReadOnlyList<Sauce>> GetSaucesAsync(CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand("SELECT id_salsa, nombre FROM salsas WHERE activa ORDER BY nombre");
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var result = new List<Sauce>();
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new Sauce(reader.GetInt64(0), reader.GetString(1)));
        }
        return result;
    }

    public async Task<CartSummary> AddToCartAsync(
        long conversationId,
        IncomingWhatsAppMessage customer,
        IReadOnlyDictionary<string, string> context,
        int quantity,
        CancellationToken cancellationToken)
    {
        var productId = RequiredLong(context, "productId");
        var deliveryType = Required(context, "deliveryType");

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        await using (var lockCommand = new NpgsqlCommand("SELECT pg_advisory_xact_lock(@conversation_id)", connection, transaction))
        {
            lockCommand.Parameters.AddWithValue("conversation_id", conversationId);
            await lockCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        var customerId = await GetOrCreateCustomerAsync(connection, transaction, customer, cancellationToken);
        var orderId = await GetOrCreateOrderAsync(connection, transaction, conversationId, customerId, customer, deliveryType, context, cancellationToken);

        const string productSql = """
            SELECT nombre_producto, precio, requiere_configuracion
            FROM productos
            WHERE id_producto = @product_id AND activo AND disponible
            FOR SHARE;
            """;
        await using var productCommand = new NpgsqlCommand(productSql, connection, transaction);
        productCommand.Parameters.AddWithValue("product_id", productId);
        await using var productReader = await productCommand.ExecuteReaderAsync(cancellationToken);
        if (!await productReader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException("El producto seleccionado ya no está disponible.");
        }
        var productName = productReader.GetString(0);
        var basePrice = productReader.GetDecimal(1);
        var requiresConfiguration = productReader.GetBoolean(2);
        await productReader.CloseAsync();

        long? optionId = null;
        long? wrappingId = null;
        long? sauceId = null;
        var finalPrice = basePrice;
        string? snapshot = null;

        if (requiresConfiguration)
        {
            optionId = RequiredLong(context, "rollOptionId");
            wrappingId = RequiredLong(context, "wrappingId");
            sauceId = RequiredLong(context, "sauceId");

            const string configurationSql = """
                SELECT o.numero_opcion, o.ingredientes, e.nombre, s.nombre, pe.precio_adicional
                FROM opciones_roll o
                JOIN producto_envolturas pe ON pe.id_producto_envoltura = @wrapping_id
                JOIN envolturas e ON e.id_envoltura = pe.id_envoltura
                JOIN salsas s ON s.id_salsa = @sauce_id
                WHERE o.id_opcion_roll = @option_id
                  AND o.id_producto = @product_id
                  AND pe.id_producto = @product_id
                  AND o.activa AND e.activa AND s.activa;
                """;
            await using var configCommand = new NpgsqlCommand(configurationSql, connection, transaction);
            configCommand.Parameters.AddWithValue("option_id", optionId.Value);
            configCommand.Parameters.AddWithValue("wrapping_id", wrappingId.Value);
            configCommand.Parameters.AddWithValue("sauce_id", sauceId.Value);
            configCommand.Parameters.AddWithValue("product_id", productId);
            await using var configReader = await configCommand.ExecuteReaderAsync(cancellationToken);
            if (!await configReader.ReadAsync(cancellationToken))
            {
                throw new InvalidOperationException("La configuración seleccionada ya no está disponible.");
            }
            var optionNumber = configReader.GetInt16(0);
            var ingredients = configReader.GetString(1);
            var wrappingName = configReader.GetString(2);
            var sauceName = configReader.GetString(3);
            finalPrice += configReader.GetDecimal(4);
            snapshot = JsonSerializer.Serialize(new
            {
                opcion = optionNumber,
                ingredientes = ingredients,
                envoltura = wrappingName,
                salsa = sauceName
            });
            await configReader.CloseAsync();
        }

        const string detailSql = """
            INSERT INTO detalle_pedidos
                (id_pedido, id_producto, nombre_producto, cantidad, precio_unitario, tipo_item)
            VALUES (@order_id, @product_id, @name, @quantity, @price, 'compra')
            RETURNING id_detalle;
            """;
        await using var detailCommand = new NpgsqlCommand(detailSql, connection, transaction);
        detailCommand.Parameters.AddWithValue("order_id", orderId);
        detailCommand.Parameters.AddWithValue("product_id", productId);
        detailCommand.Parameters.AddWithValue("name", productName);
        detailCommand.Parameters.AddWithValue("quantity", quantity);
        detailCommand.Parameters.AddWithValue("price", finalPrice);
        var detailId = (long)(await detailCommand.ExecuteScalarAsync(cancellationToken)
            ?? throw new InvalidOperationException("No se pudo crear el detalle del pedido."));

        if (requiresConfiguration)
        {
            const string rollSql = """
                INSERT INTO configuracion_roll_pedido
                    (id_detalle, id_opcion_roll, id_producto_envoltura, id_salsa, configuracion_snapshot)
                VALUES (@detail_id, @option_id, @wrapping_id, @sauce_id, @snapshot::jsonb);
                """;
            await using var rollCommand = new NpgsqlCommand(rollSql, connection, transaction);
            rollCommand.Parameters.AddWithValue("detail_id", detailId);
            rollCommand.Parameters.AddWithValue("option_id", optionId!.Value);
            rollCommand.Parameters.AddWithValue("wrapping_id", wrappingId!.Value);
            rollCommand.Parameters.AddWithValue("sauce_id", sauceId!.Value);
            rollCommand.Parameters.AddWithValue("snapshot", snapshot!);
            await rollCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        await using var totalCommand = new NpgsqlCommand("""
            UPDATE pedidos p
            SET subtotal = COALESCE((SELECT SUM(d.subtotal) FROM detalle_pedidos d WHERE d.id_pedido = p.id_pedido), 0)
            WHERE p.id_pedido = @order_id;
            """, connection, transaction);
        totalCommand.Parameters.AddWithValue("order_id", orderId);
        await totalCommand.ExecuteNonQueryAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        return (await GetCartAsync(conversationId, cancellationToken))!;
    }

    public async Task<CartSummary?> GetCartAsync(long conversationId, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT p.id_pedido, p.total, d.cantidad, d.nombre_producto, d.precio_unitario
            FROM pedidos p
            LEFT JOIN detalle_pedidos d ON d.id_pedido = p.id_pedido
            WHERE p.id_conversacion_whatsapp = @conversation_id AND p.estado = 'borrador'
            ORDER BY p.creado_en DESC, d.id_detalle;
            """;
        await using var command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue("conversation_id", conversationId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        long? orderId = null;
        decimal total = 0;
        var lines = new List<string>();
        while (await reader.ReadAsync(cancellationToken))
        {
            orderId ??= reader.GetInt64(0);
            total = reader.GetDecimal(1);
            if (!reader.IsDBNull(2))
            {
                var quantity = reader.GetInt32(2);
                var name = reader.GetString(3);
                var unitPrice = reader.GetDecimal(4);
                lines.Add($"• {quantity} × {name}: ${quantity * unitPrice:N0}");
            }
        }
        return orderId is null ? null : new CartSummary(orderId.Value, lines, total);
    }

    public async Task<long?> ConfirmCashOrderAsync(long conversationId, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        const string updateSql = """
            UPDATE pedidos
            SET estado = 'confirmado'
            WHERE id_pedido = (
                SELECT id_pedido FROM pedidos
                WHERE id_conversacion_whatsapp = @conversation_id AND estado = 'borrador'
                ORDER BY creado_en DESC LIMIT 1
                FOR UPDATE
            )
            RETURNING id_pedido, total;
            """;
        await using var updateCommand = new NpgsqlCommand(updateSql, connection, transaction);
        updateCommand.Parameters.AddWithValue("conversation_id", conversationId);
        await using var reader = await updateCommand.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }
        var orderId = reader.GetInt64(0);
        var total = reader.GetDecimal(1);
        await reader.CloseAsync();

        await using var paymentCommand = new NpgsqlCommand("""
            INSERT INTO pagos (id_pedido, monto, metodo, estado, proveedor)
            VALUES (@order_id, @amount, 'efectivo', 'pendiente', 'manual');
            """, connection, transaction);
        paymentCommand.Parameters.AddWithValue("order_id", orderId);
        paymentCommand.Parameters.AddWithValue("amount", total);
        await paymentCommand.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return orderId;
    }

    public async Task RecordOutgoingAsync(long conversationId, string? providerMessageId, string type, string content, bool sent, string? error, CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO mensajes_whatsapp
                (id_conversacion, id_mensaje_proveedor, direccion, tipo, contenido, estado, detalle_error, enviado_en)
            VALUES
                (@conversation_id, @provider_id, 'saliente', @type, @content, @status, @error,
                 CASE WHEN @sent THEN NOW() ELSE NULL END);
            """;
        await using var command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue("conversation_id", conversationId);
        command.Parameters.AddWithValue("provider_id", (object?)providerMessageId ?? DBNull.Value);
        command.Parameters.AddWithValue("type", type);
        command.Parameters.AddWithValue("content", content);
        command.Parameters.AddWithValue("status", sent ? "enviado" : "fallido");
        command.Parameters.AddWithValue("error", (object?)error ?? DBNull.Value);
        command.Parameters.AddWithValue("sent", sent);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task UpdateMessageStatusAsync(string providerMessageId, string status, DateTimeOffset occurredAt, CancellationToken cancellationToken)
    {
        var (databaseStatus, dateColumn) = status switch
        {
            "sent" => ("enviado", "enviado_en"),
            "delivered" => ("entregado", "entregado_en"),
            "read" => ("leido", "leido_en"),
            "failed" => ("fallido", (string?)null),
            _ => ((string?)null, (string?)null)
        };
        if (databaseStatus is null)
        {
            return;
        }

        var sql = dateColumn is null
            ? "UPDATE mensajes_whatsapp SET estado = @status WHERE id_mensaje_proveedor = @provider_id"
            : $"UPDATE mensajes_whatsapp SET estado = @status, {dateColumn} = @occurred_at WHERE id_mensaje_proveedor = @provider_id";
        await using var command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue("status", databaseStatus);
        command.Parameters.AddWithValue("provider_id", providerMessageId);
        if (dateColumn is not null)
        {
            command.Parameters.AddWithValue("occurred_at", occurredAt);
        }
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<IReadOnlyList<MenuProduct>> ReadProductsAsync(NpgsqlCommand command, CancellationToken cancellationToken)
    {
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var result = new List<MenuProduct>();
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new MenuProduct(
                reader.GetInt64(0), reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetString(2),
                reader.GetDecimal(3), reader.GetBoolean(4)));
        }
        return result;
    }

    private static async Task<IReadOnlyList<RollOption>> ReadRollOptionsAsync(NpgsqlCommand command, CancellationToken cancellationToken)
    {
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var result = new List<RollOption>();
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new RollOption(reader.GetInt64(0), reader.GetInt64(1), reader.GetInt16(2), reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetInt64(4)));
        }
        return result;
    }

    private static async Task<long> GetOrCreateCustomerAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        IncomingWhatsAppMessage customer,
        CancellationToken cancellationToken)
    {
        const string sql = """
            WITH existing AS (
                SELECT id_cliente FROM clientes WHERE telefono = @phone ORDER BY creado_en LIMIT 1
            ), inserted AS (
                INSERT INTO clientes (nombre, telefono)
                SELECT @name, @phone WHERE NOT EXISTS (SELECT 1 FROM existing)
                RETURNING id_cliente
            )
            SELECT id_cliente FROM existing
            UNION ALL
            SELECT id_cliente FROM inserted
            LIMIT 1;
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("name", string.IsNullOrWhiteSpace(customer.ContactName) ? customer.Phone : customer.ContactName);
        command.Parameters.AddWithValue("phone", customer.Phone);
        return (long)(await command.ExecuteScalarAsync(cancellationToken)
            ?? throw new InvalidOperationException("No se pudo obtener el cliente."));
    }

    private static async Task<long> GetOrCreateOrderAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        long conversationId,
        long customerId,
        IncomingWhatsAppMessage customer,
        string deliveryType,
        IReadOnlyDictionary<string, string> context,
        CancellationToken cancellationToken)
    {
        const string sql = """
            WITH existing AS (
                SELECT id_pedido FROM pedidos
                WHERE id_conversacion_whatsapp = @conversation_id AND estado = 'borrador'
                ORDER BY creado_en DESC LIMIT 1
            ), inserted AS (
                INSERT INTO pedidos (id_cliente, id_conversacion_whatsapp, tipo_entrega, canal_origen)
                SELECT @customer_id, @conversation_id, @delivery_type, 'whatsapp'
                WHERE NOT EXISTS (SELECT 1 FROM existing)
                RETURNING id_pedido
            )
            SELECT id_pedido FROM existing
            UNION ALL
            SELECT id_pedido FROM inserted
            LIMIT 1;
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("conversation_id", conversationId);
        command.Parameters.AddWithValue("customer_id", customerId);
        command.Parameters.AddWithValue("delivery_type", deliveryType);
        var orderId = (long)(await command.ExecuteScalarAsync(cancellationToken)
            ?? throw new InvalidOperationException("No se pudo crear el pedido."));

        if (deliveryType == "delivery")
        {
            const string deliverySql = """
                INSERT INTO entregas_pedido
                    (id_pedido, nombre_receptor, telefono_receptor, calle, numero, comuna)
                VALUES (@order_id, @name, @phone, @street, @number, @district)
                ON CONFLICT (id_pedido) DO NOTHING;
                """;
            await using var deliveryCommand = new NpgsqlCommand(deliverySql, connection, transaction);
            deliveryCommand.Parameters.AddWithValue("order_id", orderId);
            deliveryCommand.Parameters.AddWithValue("name", string.IsNullOrWhiteSpace(customer.ContactName) ? customer.Phone : customer.ContactName);
            deliveryCommand.Parameters.AddWithValue("phone", customer.Phone);
            deliveryCommand.Parameters.AddWithValue("street", Required(context, "street"));
            deliveryCommand.Parameters.AddWithValue("number", Required(context, "streetNumber"));
            deliveryCommand.Parameters.AddWithValue("district", Required(context, "district"));
            await deliveryCommand.ExecuteNonQueryAsync(cancellationToken);
        }
        return orderId;
    }

    private static IReadOnlyDictionary<string, string> DeserializeContext(string json) =>
        JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new Dictionary<string, string>();

    private static string Required(IReadOnlyDictionary<string, string> context, string key) =>
        context.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new InvalidOperationException($"Falta el dato de conversación '{key}'.");

    private static long RequiredLong(IReadOnlyDictionary<string, string> context, string key) =>
        long.TryParse(Required(context, key), NumberStyles.None, CultureInfo.InvariantCulture, out var value)
            ? value
            : throw new InvalidOperationException($"El dato de conversación '{key}' no es válido.");
}
