using System.Data;
using System.Text.Json;
using KameronSushi.Application.Abstractions;
using KameronSushi.Application.Pos;
using Npgsql;
using NpgsqlTypes;

namespace KameronSushi.Infrastructure.Persistence;

public sealed class PostgresPosStore(NpgsqlDataSource dataSource) : IPosStore
{
    public async Task<IReadOnlyList<CatalogProduct>> GetCatalogAsync(CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var products = new List<ProductBuilder>();

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT p.id_producto, p.nombre_producto, c.nombre_categoria, p.descripcion,
                       p.precio, p.disponible, p.requiere_configuracion
                  FROM productos p
                  JOIN categorias c ON c.id_categoria = p.id_categoria
                 WHERE p.activo = TRUE AND c.activa = TRUE
                 ORDER BY c.orden, p.nombre_producto;
                """;
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                products.Add(new ProductBuilder(
                    reader.GetInt64(0), reader.GetString(1), reader.GetString(2),
                    reader.IsDBNull(3) ? null : reader.GetString(3), reader.GetDecimal(4),
                    reader.GetBoolean(5), reader.GetBoolean(6)));
            }
        }

        var configurableIds = products.Where(product => product.RequiresConfiguration).Select(product => product.Id).ToArray();
        if (configurableIds.Length == 0)
        {
            return products.Select(ToCatalogProduct).ToList();
        }

        var options = new Dictionary<long, List<OptionRow>>();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT id_producto, id_opcion_roll, numero_opcion, nombre, ingredientes,
                       id_producto_envoltura_fija
                  FROM opciones_roll
                 WHERE activa = TRUE AND id_producto = ANY(@product_ids)
                 ORDER BY id_producto, numero_opcion;
                """;
            command.Parameters.AddWithValue("product_ids", configurableIds);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var row = new OptionRow(reader.GetInt64(1), reader.GetInt16(2), reader.GetString(3), reader.GetString(4),
                    reader.IsDBNull(5) ? null : reader.GetInt64(5));
                Add(options, reader.GetInt64(0), row);
            }
        }

        var wrappers = new Dictionary<long, List<WrapperRow>>();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT pe.id_producto, pe.id_producto_envoltura, e.nombre,
                       pe.precio_adicional, pe.predeterminada
                  FROM producto_envolturas pe
                  JOIN envolturas e ON e.id_envoltura = pe.id_envoltura
                 WHERE e.activa = TRUE AND pe.id_producto = ANY(@product_ids)
                 ORDER BY pe.id_producto, pe.predeterminada DESC, pe.precio_adicional, e.nombre;
                """;
            command.Parameters.AddWithValue("product_ids", configurableIds);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                Add(wrappers, reader.GetInt64(0), new WrapperRow(
                    reader.GetInt64(1), reader.GetString(2), reader.GetDecimal(3), reader.GetBoolean(4)));
            }
        }

        long sauceId;
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT id_salsa FROM salsas WHERE activa = TRUE ORDER BY CASE WHEN nombre = 'Soya' THEN 0 ELSE 1 END, id_salsa LIMIT 1;";
            sauceId = Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken)
                ?? throw new InvalidOperationException("No existe una salsa activa para configurar los productos."));
        }

        foreach (var product in products.Where(product => product.RequiresConfiguration))
        {
            if (!options.TryGetValue(product.Id, out var productOptions) || productOptions.Count == 0 ||
                !wrappers.TryGetValue(product.Id, out var productWrappers) || productWrappers.Count == 0)
            {
                product.Available = false;
                continue;
            }

            if (product.Category.Equals("Hand rolls", StringComparison.OrdinalIgnoreCase))
            {
                var option = productOptions[0];
                product.Selections.AddRange(productWrappers.Select(wrapper => new CatalogSelection(
                    option.Id, wrapper.Id, sauceId, wrapper.Name,
                    $"Envoltura: {wrapper.Name}", wrapper.PriceAdjustment)));
                continue;
            }

            foreach (var option in productOptions)
            {
                var wrapper = option.FixedWrapperId is long fixedWrapperId
                    ? productWrappers.FirstOrDefault(value => value.Id == fixedWrapperId)
                    : productWrappers.FirstOrDefault(value => value.IsDefault) ?? productWrappers[0];
                if (wrapper is null) continue;

                var label = $"N.º {option.Number} · {option.Ingredients}";
                var details = option.FixedWrapperId is null
                    ? label
                    : $"{label} · Envoltura: {wrapper.Name}";
                product.Selections.Add(new CatalogSelection(
                    option.Id, wrapper.Id, sauceId, label, details, wrapper.PriceAdjustment));
            }

            if (product.Selections.Count == 0) product.Available = false;
        }

        return products.Select(ToCatalogProduct).ToList();
    }

    public async Task<CreatedOrder> CreateLocalOrderAsync(CreateLocalOrder command, CancellationToken cancellationToken)
    {
        if (command.Items is null || command.Items.Count == 0) throw new ArgumentException("El pedido debe contener al menos un producto.");
        if (command.Payments is null || command.Payments.Count == 0) throw new ArgumentException("El pedido debe contener al menos un pago.");
        if (command.Items.Count > 100) throw new ArgumentException("El pedido no puede contener más de 100 líneas.");
        if (command.Payments.Count > 10) throw new ArgumentException("El pedido no puede contener más de 10 pagos.");
        if (command.Items.Any(item => item.Quantity is <= 0 or > 100)) throw new ArgumentException("Cada cantidad debe estar entre 1 y 100.");
        if (command.Payments.Any(payment => payment.Amount <= 0)) throw new ArgumentException("Los pagos deben ser mayores que cero.");
        if (command.Payments.Any(payment => string.IsNullOrWhiteSpace(payment.Method))) throw new ArgumentException("Cada pago debe indicar un método.");

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);

        var validatedItems = new List<ValidatedItem>();
        foreach (var item in command.Items)
        {
            validatedItems.Add(await ValidateItemAsync(connection, transaction, item, cancellationToken));
        }

        var subtotal = validatedItems.Sum(item => item.UnitPrice * item.Quantity);
        var normalizedPayments = command.Payments
            .Select(payment => new NormalizedPayment(NormalizePaymentMethod(payment.Method), payment.Amount))
            .ToList();
        var paid = normalizedPayments.Sum(payment => payment.Amount);
        if (paid < subtotal) throw new ArgumentException("El monto pagado es menor que el total del pedido.");
        var cashPaid = normalizedPayments.Where(payment => payment.Method == "efectivo").Sum(payment => payment.Amount);
        if (paid - subtotal > cashPaid)
        {
            throw new ArgumentException("Los pagos electrónicos no pueden generar vuelto.");
        }

        long orderId;
        DateTimeOffset createdAt;
        await using (var orderCommand = connection.CreateCommand())
        {
            orderCommand.Transaction = transaction;
            orderCommand.CommandText = """
                INSERT INTO pedidos (estado, subtotal, descuento, tipo_entrega, canal_origen, costo_envio)
                VALUES ('en_preparacion', @subtotal, 0, 'retiro', 'local', 0)
                RETURNING id_pedido, creado_en;
                """;
            orderCommand.Parameters.AddWithValue("subtotal", subtotal);
            await using var reader = await orderCommand.ExecuteReaderAsync(cancellationToken);
            await reader.ReadAsync(cancellationToken);
            orderId = reader.GetInt64(0);
            createdAt = reader.GetFieldValue<DateTimeOffset>(1);
        }

        foreach (var item in validatedItems)
        {
            long detailId;
            await using (var detailCommand = connection.CreateCommand())
            {
                detailCommand.Transaction = transaction;
                detailCommand.CommandText = """
                    INSERT INTO detalle_pedidos
                        (id_pedido, id_producto, nombre_producto, cantidad, precio_unitario, observaciones)
                    VALUES (@order_id, @product_id, @name, @quantity, @unit_price, @details)
                    RETURNING id_detalle;
                    """;
                detailCommand.Parameters.AddWithValue("order_id", orderId);
                detailCommand.Parameters.AddWithValue("product_id", item.ProductId);
                detailCommand.Parameters.AddWithValue("name", item.Name);
                detailCommand.Parameters.AddWithValue("quantity", item.Quantity);
                detailCommand.Parameters.AddWithValue("unit_price", item.UnitPrice);
                detailCommand.Parameters.Add("details", NpgsqlDbType.Text).Value = (object?)item.Details ?? DBNull.Value;
                detailId = Convert.ToInt64(await detailCommand.ExecuteScalarAsync(cancellationToken));
            }

            if (!item.RequiresConfiguration) continue;

            await using var configurationCommand = connection.CreateCommand();
            configurationCommand.Transaction = transaction;
            configurationCommand.CommandText = """
                INSERT INTO configuracion_roll_pedido
                    (id_detalle, id_opcion_roll, id_producto_envoltura, id_salsa, configuracion_snapshot)
                VALUES (@detail_id, @option_id, @wrapper_id, @sauce_id, @snapshot);
                """;
            configurationCommand.Parameters.AddWithValue("detail_id", detailId);
            configurationCommand.Parameters.AddWithValue("option_id", item.OptionId!.Value);
            configurationCommand.Parameters.AddWithValue("wrapper_id", item.ProductWrapperId!.Value);
            configurationCommand.Parameters.AddWithValue("sauce_id", item.SauceId!.Value);
            configurationCommand.Parameters.AddWithValue("snapshot", NpgsqlDbType.Jsonb, JsonSerializer.Serialize(new
            {
                opcion = item.OptionNumber,
                ingredientes = item.Ingredients,
                envoltura = item.WrapperName,
                salsa = item.SauceName
            }));
            await configurationCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        foreach (var payment in normalizedPayments)
        {
            await using var paymentCommand = connection.CreateCommand();
            paymentCommand.Transaction = transaction;
            paymentCommand.CommandText = """
                INSERT INTO pagos
                    (id_pedido, monto, metodo, estado, pagado_en, proveedor, moneda)
                VALUES (@order_id, @amount, @method, 'aprobado', NOW(), 'manual', 'CLP');
                """;
            paymentCommand.Parameters.AddWithValue("order_id", orderId);
            paymentCommand.Parameters.AddWithValue("amount", payment.Amount);
            paymentCommand.Parameters.AddWithValue("method", payment.Method);
            await paymentCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return new CreatedOrder(orderId, subtotal, paid, Math.Max(paid - subtotal, 0), createdAt);
    }

    public async Task<IReadOnlyList<PosOrderSummary>> GetOrdersAsync(
        string? status,
        int limit,
        CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand("""
            SELECT p.id_pedido, p.estado, p.tipo_entrega, p.canal_origen, p.total,
                   COALESCE(SUM(d.cantidad), 0)::integer AS cantidad_items,
                   c.nombre, p.creado_en
              FROM pedidos p
              LEFT JOIN clientes c ON c.id_cliente = p.id_cliente
              LEFT JOIN detalle_pedidos d ON d.id_pedido = p.id_pedido
             WHERE (@status IS NULL OR p.estado = @status)
             GROUP BY p.id_pedido, c.nombre
             ORDER BY p.creado_en DESC
             LIMIT @limit;
            """);
        command.Parameters.Add("status", NpgsqlDbType.Varchar).Value = (object?)status ?? DBNull.Value;
        command.Parameters.AddWithValue("limit", limit);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var orders = new List<PosOrderSummary>();
        while (await reader.ReadAsync(cancellationToken))
        {
            orders.Add(new PosOrderSummary(
                reader.GetInt64(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
                reader.GetDecimal(4), reader.GetInt32(5), reader.IsDBNull(6) ? null : reader.GetString(6),
                reader.GetFieldValue<DateTimeOffset>(7)));
        }
        return orders;
    }

    public async Task<PosOrderDetails?> GetOrderAsync(long orderId, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        PosOrderHeader? header;
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT p.id_pedido, p.estado, p.tipo_entrega, p.canal_origen,
                       p.subtotal, p.descuento, p.costo_envio, p.total, p.observaciones,
                       c.nombre, c.telefono, p.creado_en
                  FROM pedidos p
                  LEFT JOIN clientes c ON c.id_cliente = p.id_cliente
                 WHERE p.id_pedido = @order_id;
                """;
            command.Parameters.AddWithValue("order_id", orderId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken)) return null;
            header = new PosOrderHeader(
                reader.GetInt64(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
                reader.GetDecimal(4), reader.GetDecimal(5), reader.GetDecimal(6), reader.GetDecimal(7),
                reader.IsDBNull(8) ? null : reader.GetString(8),
                reader.IsDBNull(9) ? null : reader.GetString(9),
                reader.IsDBNull(10) ? null : reader.GetString(10),
                reader.GetFieldValue<DateTimeOffset>(11));
        }

        var items = new List<PosOrderItem>();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT id_detalle, id_producto, nombre_producto, cantidad,
                       precio_unitario, subtotal, observaciones
                  FROM detalle_pedidos
                 WHERE id_pedido = @order_id
                 ORDER BY id_detalle;
                """;
            command.Parameters.AddWithValue("order_id", orderId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                items.Add(new PosOrderItem(
                    reader.GetInt64(0), reader.GetInt64(1), reader.GetString(2), reader.GetInt32(3),
                    reader.GetDecimal(4), reader.GetDecimal(5), reader.IsDBNull(6) ? null : reader.GetString(6)));
            }
        }

        var payments = new List<PosOrderPayment>();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT id_pago, metodo, estado, monto, creado_en, pagado_en
                  FROM pagos
                 WHERE id_pedido = @order_id
                 ORDER BY creado_en, id_pago;
                """;
            command.Parameters.AddWithValue("order_id", orderId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                payments.Add(new PosOrderPayment(
                    reader.GetInt64(0), reader.GetString(1), reader.GetString(2), reader.GetDecimal(3),
                    reader.GetFieldValue<DateTimeOffset>(4),
                    reader.IsDBNull(5) ? null : reader.GetFieldValue<DateTimeOffset>(5)));
            }
        }

        return new PosOrderDetails(
            header.Id, header.Status, header.DeliveryType, header.Channel,
            header.Subtotal, header.Discount, header.ShippingCost, header.Total,
            header.Notes, header.CustomerName, header.CustomerPhone, header.CreatedAt,
            items, payments);
    }

    public async Task<IReadOnlyList<KitchenOrderSummary>> GetKitchenOrdersAsync(CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand("""
            SELECT p.id_pedido, p.creado_en, d.cantidad, d.nombre_producto, d.observaciones
              FROM pedidos p
              JOIN detalle_pedidos d ON d.id_pedido = p.id_pedido
             WHERE p.estado IN ('confirmado', 'en_preparacion')
             ORDER BY p.creado_en, d.id_detalle;
            """);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var orders = new Dictionary<long, KitchenBuilder>();
        while (await reader.ReadAsync(cancellationToken))
        {
            var orderId = reader.GetInt64(0);
            if (!orders.TryGetValue(orderId, out var order))
            {
                order = new KitchenBuilder(orderId, reader.GetFieldValue<DateTimeOffset>(1));
                orders.Add(orderId, order);
            }

            var quantity = reader.GetInt32(2);
            var name = reader.GetString(3);
            var details = reader.IsDBNull(4) ? null : reader.GetString(4);
            var text = string.IsNullOrWhiteSpace(details) ? name : $"{name} — {details}";
            order.Products.Add(quantity == 1 ? text : $"{quantity} × {text}");
        }

        return orders.Values.Select(order => new KitchenOrderSummary(order.Id, order.CreatedAt, order.Products)).ToList();
    }

    public async Task<bool> MarkOrderReadyAsync(long orderId, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand("""
            UPDATE pedidos
               SET estado = 'listo', listo_en = NOW()
             WHERE id_pedido = @order_id
               AND estado IN ('confirmado', 'en_preparacion');
            """);
        command.Parameters.AddWithValue("order_id", orderId);
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    private static async Task<ValidatedItem> ValidateItemAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        CreateLocalOrderItem item,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT p.nombre_producto, p.precio, p.requiere_configuracion,
                   o.numero_opcion, o.ingredientes, o.id_producto_envoltura_fija,
                   pe.precio_adicional, e.nombre, s.nombre, p.descripcion
              FROM productos p
              LEFT JOIN opciones_roll o
                ON o.id_opcion_roll = @option_id AND o.id_producto = p.id_producto AND o.activa = TRUE
              LEFT JOIN producto_envolturas pe
                ON pe.id_producto_envoltura = @wrapper_id AND pe.id_producto = p.id_producto
              LEFT JOIN envolturas e ON e.id_envoltura = pe.id_envoltura AND e.activa = TRUE
              LEFT JOIN salsas s ON s.id_salsa = @sauce_id AND s.activa = TRUE
             WHERE p.id_producto = @product_id AND p.activo = TRUE AND p.disponible = TRUE;
            """;
        command.Parameters.AddWithValue("product_id", item.ProductId);
        command.Parameters.Add("option_id", NpgsqlDbType.Bigint).Value = (object?)item.OptionId ?? DBNull.Value;
        command.Parameters.Add("wrapper_id", NpgsqlDbType.Bigint).Value = (object?)item.ProductWrapperId ?? DBNull.Value;
        command.Parameters.Add("sauce_id", NpgsqlDbType.Bigint).Value = (object?)item.SauceId ?? DBNull.Value;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new ArgumentException($"El producto {item.ProductId} no existe o no está disponible.");
        }

        var name = reader.GetString(0);
        var basePrice = reader.GetDecimal(1);
        var requiresConfiguration = reader.GetBoolean(2);
        if (!requiresConfiguration)
        {
            if (item.OptionId is not null || item.ProductWrapperId is not null || item.SauceId is not null)
            {
                throw new ArgumentException($"El producto {name} no admite configuración de roll.");
            }
            var description = reader.IsDBNull(9) ? null : reader.GetString(9);
            return new ValidatedItem(item.ProductId, name, item.Quantity, basePrice, false,
                null, null, null, null, null, null, null, description);
        }

        if (reader.IsDBNull(3) || reader.IsDBNull(6) || reader.IsDBNull(7) || reader.IsDBNull(8))
        {
            throw new ArgumentException($"La configuración elegida para {name} no es válida.");
        }

        long? fixedWrapperId = reader.IsDBNull(5) ? null : reader.GetInt64(5);
        if (fixedWrapperId is not null && fixedWrapperId != item.ProductWrapperId)
        {
            throw new ArgumentException($"La opción elegida para {name} requiere su envoltura fija.");
        }

        var optionNumber = reader.GetInt16(3);
        var ingredients = reader.GetString(4);
        var wrapperPrice = reader.GetDecimal(6);
        var wrapperName = reader.GetString(7);
        var sauceName = reader.GetString(8);
        var details = $"N.º {optionNumber} · {ingredients} · Envoltura: {wrapperName} · Salsa: {sauceName}";
        return new ValidatedItem(item.ProductId, name, item.Quantity, basePrice + wrapperPrice, true,
            item.OptionId, item.ProductWrapperId, item.SauceId, optionNumber, ingredients, wrapperName, sauceName, details);
    }

    private static string NormalizePaymentMethod(string method) => method.Trim().ToLowerInvariant() switch
    {
        "efectivo" => "efectivo",
        "tarjeta" => "tarjeta",
        "edenred" => "edenred",
        _ => throw new ArgumentException($"El método de pago '{method}' no es válido.")
    };

    private static CatalogProduct ToCatalogProduct(ProductBuilder product) =>
        new(product.Id, product.Name, product.Category, product.Description, product.Price,
            product.Available, product.Selections);

    private static void Add<T>(Dictionary<long, List<T>> values, long key, T item)
    {
        if (!values.TryGetValue(key, out var list))
        {
            list = [];
            values.Add(key, list);
        }
        list.Add(item);
    }

    private sealed class ProductBuilder(long id, string name, string category, string? description, decimal price, bool available, bool requiresConfiguration)
    {
        public long Id { get; } = id;
        public string Name { get; } = name;
        public string Category { get; } = category;
        public string? Description { get; } = description;
        public decimal Price { get; } = price;
        public bool Available { get; set; } = available;
        public bool RequiresConfiguration { get; } = requiresConfiguration;
        public List<CatalogSelection> Selections { get; } = [];
    }

    private sealed record OptionRow(long Id, short Number, string Name, string Ingredients, long? FixedWrapperId);
    private sealed record WrapperRow(long Id, string Name, decimal PriceAdjustment, bool IsDefault);
    private sealed record ValidatedItem(
        long ProductId, string Name, int Quantity, decimal UnitPrice, bool RequiresConfiguration,
        long? OptionId, long? ProductWrapperId, long? SauceId, short? OptionNumber,
        string? Ingredients, string? WrapperName, string? SauceName, string? Details = null);
    private sealed record KitchenBuilder(long Id, DateTimeOffset CreatedAt)
    {
        public List<string> Products { get; } = [];
    }
    private sealed record NormalizedPayment(string Method, decimal Amount);
    private sealed record PosOrderHeader(
        long Id, string Status, string DeliveryType, string Channel,
        decimal Subtotal, decimal Discount, decimal ShippingCost, decimal Total,
        string? Notes, string? CustomerName, string? CustomerPhone, DateTimeOffset CreatedAt);
}
