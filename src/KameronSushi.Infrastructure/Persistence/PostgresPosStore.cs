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

        var sauces = new List<CatalogSauce>();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT id_salsa, nombre, descripcion FROM salsas WHERE activa = TRUE ORDER BY nombre;";
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                sauces.Add(new CatalogSauce(
                    reader.GetInt64(0), reader.GetString(1),
                    reader.IsDBNull(2) ? null : reader.GetString(2)));
            }
        }

        foreach (var product in products.Where(product => product.RequiresConfiguration))
        {
            if (!options.TryGetValue(product.Id, out var productOptions) || productOptions.Count == 0 ||
                !wrappers.TryGetValue(product.Id, out var productWrappers) || productWrappers.Count == 0 ||
                sauces.Count == 0)
            {
                product.Available = false;
                continue;
            }

            product.Options.AddRange(productOptions.Select(option => new CatalogRollOption(
                option.Id, option.Number, option.Name, option.Ingredients, option.FixedWrapperId)));
            product.Wrappers.AddRange(productWrappers.Select(wrapper => new CatalogWrapper(
                wrapper.Id, wrapper.Name, wrapper.PriceAdjustment, wrapper.IsDefault)));
            product.Sauces.AddRange(sauces);

            var defaultSauce = sauces.FirstOrDefault(sauce => sauce.Name.Equals("Soya", StringComparison.OrdinalIgnoreCase))
                ?? sauces[0];
            if (product.Category.Equals("Hand rolls", StringComparison.OrdinalIgnoreCase))
            {
                var option = productOptions[0];
                product.Selections.AddRange(productWrappers.Select(wrapper => new CatalogSelection(
                    option.Id, wrapper.Id, defaultSauce.SauceId, wrapper.Name,
                    $"Envoltura: {wrapper.Name}", wrapper.PriceAdjustment)));
            }
            else
            {
                foreach (var option in productOptions)
                {
                    var wrapper = option.FixedWrapperId is long fixedWrapperId
                        ? productWrappers.FirstOrDefault(value => value.Id == fixedWrapperId)
                        : productWrappers.FirstOrDefault(value => value.IsDefault) ?? productWrappers[0];
                    if (wrapper is null) continue;
                    var label = $"N.º {option.Number} · {option.Ingredients}";
                    var details = option.FixedWrapperId is null ? label : $"{label} · Envoltura: {wrapper.Name}";
                    product.Selections.Add(new CatalogSelection(
                        option.Id, wrapper.Id, defaultSauce.SauceId, label, details, wrapper.PriceAdjustment));
                }
            }
        }

        return products.Select(ToCatalogProduct).ToList();
    }

    public async Task<CustomerLoyalty?> GetCustomerLoyaltyByPhoneAsync(string phone, CancellationToken cancellationToken)
    {
        var digits = new string(phone.Where(char.IsDigit).ToArray());
        if (digits.Length < 8) return null;
        var normalized = digits.Length > 9 ? digits[^9..] : digits;

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        long customerId;
        string name;
        string storedPhone;
        int points;
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT c.id_cliente, c.nombre, c.telefono,
                       COALESCE(cf.saldo_puntos, 0)
                  FROM clientes c
                  LEFT JOIN cuentas_fidelizacion cf
                    ON cf.id_cliente = c.id_cliente AND cf.estado = 'activa'
                 WHERE RIGHT(REGEXP_REPLACE(c.telefono, '\D', '', 'g'), 9) = @phone
                 ORDER BY c.creado_en
                 LIMIT 1;
                """;
            command.Parameters.AddWithValue("phone", normalized);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken)) return null;
            customerId = reader.GetInt64(0);
            name = reader.GetString(1);
            storedPhone = reader.GetString(2);
            points = reader.GetInt32(3);
        }

        var rewards = new List<RewardCatalogItem>();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT pc.id_producto_canje, p.id_producto, p.nombre_producto, p.descripcion,
                       pc.coste_puntos, pc.stock_disponible, pc.limite_por_pedido
                  FROM productos_canje pc
                  JOIN productos p ON p.id_producto = pc.id_producto
                 WHERE pc.activo = TRUE
                   AND pc.vigente_desde <= NOW()
                   AND (pc.vigente_hasta IS NULL OR pc.vigente_hasta > NOW())
                   AND (pc.stock_disponible IS NULL OR pc.stock_disponible > 0)
                   AND p.activo = TRUE AND p.disponible = TRUE
                   AND p.requiere_configuracion = FALSE
                 ORDER BY pc.coste_puntos, p.nombre_producto;
                """;
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                rewards.Add(new RewardCatalogItem(
                    reader.GetInt64(0), reader.GetInt64(1), reader.GetString(2),
                    reader.IsDBNull(3) ? null : reader.GetString(3), reader.GetInt32(4),
                    reader.IsDBNull(5) ? null : reader.GetInt32(5),
                    reader.IsDBNull(6) ? null : reader.GetInt32(6)));
            }
        }

        return new CustomerLoyalty(customerId, name, storedPhone, points, rewards);
    }

    public async Task<CreatedOrder> CreateLocalOrderAsync(CreateLocalOrder command, CancellationToken cancellationToken)
    {
        var purchaseItems = command.Items ?? [];
        var rewardItems = (command.Rewards ?? [])
            .GroupBy(item => item.RewardProductId)
            .Select(group => new CreateRewardItem(group.Key, group.Sum(item => item.Quantity)))
            .ToList();
        var payments = command.Payments ?? [];
        if (purchaseItems.Count == 0 && rewardItems.Count == 0) throw new ArgumentException("El pedido debe contener al menos un producto.");
        if (purchaseItems.Count > 100) throw new ArgumentException("El pedido no puede contener más de 100 líneas.");
        if (rewardItems.Count > 20) throw new ArgumentException("El pedido no puede contener más de 20 líneas de canje.");
        if (payments.Count > 10) throw new ArgumentException("El pedido no puede contener más de 10 pagos.");
        if (purchaseItems.Any(item => item.Quantity is <= 0 or > 100) || rewardItems.Any(item => item.Quantity is <= 0 or > 100))
            throw new ArgumentException("Cada cantidad debe estar entre 1 y 100.");
        if (payments.Any(payment => payment.Amount <= 0)) throw new ArgumentException("Los pagos deben ser mayores que cero.");
        if (payments.Any(payment => string.IsNullOrWhiteSpace(payment.Method))) throw new ArgumentException("Cada pago debe indicar un método.");
        if (rewardItems.Count > 0 && command.CustomerId is null) throw new ArgumentException("Selecciona un cliente para canjear puntos.");

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);

        var shiftId = await GetOpenShiftIdAsync(connection, transaction, cancellationToken)
            ?? throw new ArgumentException("No hay un turno de caja abierto. Inicia el turno antes de crear pedidos.");

        var validatedItems = new List<ValidatedItem>();
        foreach (var item in purchaseItems)
        {
            validatedItems.Add(await ValidateItemAsync(connection, transaction, item, cancellationToken));
        }

        if (command.CustomerId is not null && !await CustomerExistsAsync(connection, transaction, command.CustomerId.Value, cancellationToken))
            throw new ArgumentException("El cliente seleccionado no existe.");

        LoyaltyAccount? loyaltyAccount = null;
        var validatedRewards = new List<ValidatedReward>();
        if (rewardItems.Count > 0)
        {
            loyaltyAccount = await LockLoyaltyAccountAsync(connection, transaction, command.CustomerId!.Value, cancellationToken)
                ?? throw new ArgumentException("El cliente no tiene una cuenta de puntos activa.");
            foreach (var reward in rewardItems)
                validatedRewards.Add(await ValidateRewardAsync(connection, transaction, reward, cancellationToken));
            var requiredPoints = validatedRewards.Sum(reward => reward.PointsCost * reward.Quantity);
            if (requiredPoints > loyaltyAccount.Points) throw new ArgumentException("El cliente no tiene puntos suficientes para este canje.");
        }

        var subtotal = validatedItems.Sum(item => item.UnitPrice * item.Quantity);
        var normalizedPayments = payments
            .Select(payment => new NormalizedPayment(NormalizePaymentMethod(payment.Method), payment.Amount))
            .ToList();
        var paid = normalizedPayments.Sum(payment => payment.Amount);
        if (subtotal > 0 && normalizedPayments.Count == 0) throw new ArgumentException("El pedido debe contener al menos un pago.");
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
                INSERT INTO pedidos (id_cliente, id_turno, estado, subtotal, descuento, tipo_entrega, canal_origen, costo_envio)
                VALUES (@customer_id, @shift_id, 'en_preparacion', @subtotal, 0, 'retiro', 'local', 0)
                RETURNING id_pedido, creado_en;
                """;
            orderCommand.Parameters.AddWithValue("subtotal", subtotal);
            orderCommand.Parameters.AddWithValue("shift_id", shiftId);
            orderCommand.Parameters.Add("customer_id", NpgsqlDbType.Bigint).Value = (object?)command.CustomerId ?? DBNull.Value;
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

        foreach (var reward in validatedRewards)
        {
            await using var detailCommand = connection.CreateCommand();
            detailCommand.Transaction = transaction;
            detailCommand.CommandText = """
                INSERT INTO detalle_pedidos
                    (id_pedido, id_producto, nombre_producto, cantidad, precio_unitario,
                     puntos_unitarios, tipo_item, observaciones)
                VALUES (@order_id, @product_id, @name, @quantity, 0,
                        @points, 'canje', 'Producto canjeado con puntos');

                UPDATE productos_canje
                   SET stock_disponible = CASE
                           WHEN stock_disponible IS NULL THEN NULL
                           ELSE stock_disponible - @quantity
                       END,
                       actualizado_en = NOW()
                 WHERE id_producto_canje = @reward_id;
                """;
            detailCommand.Parameters.AddWithValue("order_id", orderId);
            detailCommand.Parameters.AddWithValue("product_id", reward.ProductId);
            detailCommand.Parameters.AddWithValue("name", reward.Name);
            detailCommand.Parameters.AddWithValue("quantity", reward.Quantity);
            detailCommand.Parameters.AddWithValue("points", reward.PointsCost);
            detailCommand.Parameters.AddWithValue("reward_id", reward.RewardProductId);
            await detailCommand.ExecuteNonQueryAsync(cancellationToken);
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

        int? remainingPoints = null;
        if (loyaltyAccount is not null)
        {
            var redeemedPoints = validatedRewards.Sum(reward => reward.PointsCost * reward.Quantity);
            remainingPoints = loyaltyAccount.Points - redeemedPoints;
            await using var pointsCommand = connection.CreateCommand();
            pointsCommand.Transaction = transaction;
            pointsCommand.CommandText = """
                UPDATE cuentas_fidelizacion
                   SET saldo_puntos = @new_balance,
                       puntos_canjeados = puntos_canjeados + @redeemed,
                       actualizado_en = NOW()
                 WHERE id_cuenta = @account_id;

                INSERT INTO movimiento_puntos
                    (id_cuenta, id_pedido, tipo, puntos, saldo_anterior, saldo_resultante, descripcion)
                VALUES (@account_id, @order_id, 'canje', -@redeemed, @previous_balance,
                        @new_balance, 'Canje realizado en caja');
                """;
            pointsCommand.Parameters.AddWithValue("new_balance", remainingPoints.Value);
            pointsCommand.Parameters.AddWithValue("redeemed", redeemedPoints);
            pointsCommand.Parameters.AddWithValue("account_id", loyaltyAccount.AccountId);
            pointsCommand.Parameters.AddWithValue("order_id", orderId);
            pointsCommand.Parameters.AddWithValue("previous_balance", loyaltyAccount.Points);
            await pointsCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return new CreatedOrder(orderId, subtotal, paid, Math.Max(paid - subtotal, 0), createdAt, remainingPoints);
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
            SELECT p.id_pedido, p.tipo_entrega, p.creado_en, d.cantidad, d.nombre_producto, d.observaciones
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
                order = new KitchenBuilder(orderId, reader.GetString(1), reader.GetFieldValue<DateTimeOffset>(2));
                orders.Add(orderId, order);
            }

            var quantity = reader.GetInt32(3);
            var name = reader.GetString(4);
            var details = reader.IsDBNull(5) ? null : reader.GetString(5);
            var text = string.IsNullOrWhiteSpace(details) ? name : $"{name} — {details}";
            order.Products.Add(quantity == 1 ? text : $"{quantity} × {text}");
        }

        return orders.Values.Select(order => new KitchenOrderSummary(order.Id, order.DeliveryType, order.CreatedAt, order.Products)).ToList();
    }

    public async Task<IReadOnlyList<KitchenPerformanceSummary>> GetKitchenPerformanceAsync(CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand("""
            SELECT tipo_entrega,
                   COUNT(*)::int,
                   AVG(EXTRACT(EPOCH FROM (listo_en - creado_en)) / 60.0)::double precision
              FROM pedidos
             WHERE listo_en IS NOT NULL
               AND id_turno = (SELECT id_turno FROM turnos WHERE estado = 'abierto' LIMIT 1)
               AND tipo_entrega IN ('delivery', 'retiro')
             GROUP BY tipo_entrega;
            """);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var performance = new List<KitchenPerformanceSummary>();
        while (await reader.ReadAsync(cancellationToken))
        {
            performance.Add(new KitchenPerformanceSummary(
                reader.GetString(0), reader.GetInt32(1),
                reader.IsDBNull(2) ? null : reader.GetDouble(2)));
        }

        return performance;
    }

    public async Task<CashShift?> GetCurrentShiftAsync(CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand("""
            SELECT t.id_turno, t.estado, t.abierto_en, t.cerrado_en,
                   t.id_usuario_apertura, u.nombre, t.monto_inicial
              FROM turnos t
              JOIN usuarios u ON u.id_usuario = t.id_usuario_apertura
             WHERE t.estado = 'abierto'
             LIMIT 1;
            """);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadShift(reader) : null;
    }

    public async Task<CashShift> OpenShiftAsync(decimal openingAmount, CancellationToken cancellationToken)
    {
        if (openingAmount < 0) throw new ArgumentException("El monto inicial no puede ser negativo.");
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);

        var existing = await GetOpenShiftAsync(connection, transaction, false, cancellationToken);
        if (existing is not null)
        {
            await transaction.CommitAsync(cancellationToken);
            return existing;
        }

        long userId;
        await using (var userCommand = connection.CreateCommand())
        {
            userCommand.Transaction = transaction;
            userCommand.CommandText = """
                INSERT INTO usuarios (nombre, email, password_hash, estado)
                VALUES ('Caja local', 'caja.local@kameronsushi.internal', 'AUTENTICACION_PENDIENTE', 'activo')
                ON CONFLICT ((lower(email))) DO UPDATE
                    SET estado = 'activo', actualizado_en = NOW()
                RETURNING id_usuario;
                """;
            userId = Convert.ToInt64(await userCommand.ExecuteScalarAsync(cancellationToken));
        }

        CashShift shift;
        await using (var shiftCommand = connection.CreateCommand())
        {
            shiftCommand.Transaction = transaction;
            shiftCommand.CommandText = """
                INSERT INTO turnos (id_usuario_apertura, estado, monto_inicial)
                VALUES (@user_id, 'abierto', @opening_amount)
                RETURNING id_turno, estado, abierto_en, cerrado_en, id_usuario_apertura, monto_inicial;
                """;
            shiftCommand.Parameters.AddWithValue("user_id", userId);
            shiftCommand.Parameters.AddWithValue("opening_amount", openingAmount);
            await using var reader = await shiftCommand.ExecuteReaderAsync(cancellationToken);
            await reader.ReadAsync(cancellationToken);
            shift = new CashShift(reader.GetInt64(0), reader.GetString(1),
                reader.GetFieldValue<DateTimeOffset>(2), null, reader.GetInt64(4), "Caja local", reader.GetDecimal(5));
        }

        await transaction.CommitAsync(cancellationToken);
        return shift;
    }

    public async Task<CashMovement?> AddCashMovementAsync(
        long shiftId, CreateCashMovement movement, CancellationToken cancellationToken)
    {
        var type = movement.Type.Trim().ToLowerInvariant();
        if (type is not ("ingreso" or "retiro" or "gasto"))
            throw new ArgumentException("El tipo de movimiento debe ser ingreso, retiro o gasto.");
        if (movement.Amount <= 0) throw new ArgumentException("El monto del movimiento debe ser mayor que cero.");
        if (string.IsNullOrWhiteSpace(movement.Reason)) throw new ArgumentException("Ingresa el motivo del movimiento.");
        if (movement.Reason.Trim().Length > 250) throw new ArgumentException("El motivo no puede superar 250 caracteres.");

        await using var command = dataSource.CreateCommand("""
            INSERT INTO movimientos_caja (id_turno, tipo, monto, motivo)
            SELECT @shift_id, @type, @amount, @reason
             WHERE EXISTS (SELECT 1 FROM turnos WHERE id_turno = @shift_id AND estado = 'abierto')
            RETURNING id_movimiento, tipo, monto, motivo, creado_en;
            """);
        command.Parameters.AddWithValue("shift_id", shiftId);
        command.Parameters.AddWithValue("type", type);
        command.Parameters.AddWithValue("amount", movement.Amount);
        command.Parameters.AddWithValue("reason", movement.Reason.Trim());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new CashMovement(reader.GetInt64(0), reader.GetString(1), reader.GetDecimal(2), reader.GetString(3), reader.GetFieldValue<DateTimeOffset>(4))
            : null;
    }

    public async Task<CashShiftReport?> GetShiftReportAsync(long shiftId, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        return await BuildShiftReportAsync(connection, null, shiftId, cancellationToken);
    }

    public async Task<CashShiftReport?> CloseShiftAsync(
        long shiftId, CloseCashShift request, CancellationToken cancellationToken)
    {
        if (request.CountedCash < 0) throw new ArgumentException("El efectivo contado no puede ser negativo.");
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        var current = await GetOpenShiftAsync(connection, transaction, true, cancellationToken);
        if (current is null || current.ShiftId != shiftId)
        {
            await transaction.RollbackAsync(cancellationToken);
            return null;
        }

        var report = await BuildShiftReportAsync(connection, transaction, shiftId, cancellationToken)
            ?? throw new InvalidOperationException("No se pudo calcular el arqueo del turno.");
        var difference = request.CountedCash - report.ExpectedCash;
        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                UPDATE turnos
                   SET estado = 'cerrado', cerrado_en = NOW(),
                       id_usuario_cierre = id_usuario_apertura,
                       efectivo_contado = @counted_cash,
                       efectivo_esperado = @expected_cash,
                       diferencia_efectivo = @difference,
                       observaciones = @notes,
                       actualizado_en = NOW()
                 WHERE id_turno = @shift_id AND estado = 'abierto'
                RETURNING id_turno;
                """;
            command.Parameters.AddWithValue("shift_id", shiftId);
            command.Parameters.AddWithValue("counted_cash", request.CountedCash);
            command.Parameters.AddWithValue("expected_cash", report.ExpectedCash);
            command.Parameters.AddWithValue("difference", difference);
            command.Parameters.Add("notes", NpgsqlDbType.Text).Value = (object?)request.Notes?.Trim() ?? DBNull.Value;
            if (await command.ExecuteScalarAsync(cancellationToken) is null) return null;
        }

        await transaction.CommitAsync(cancellationToken);
        return await GetShiftReportAsync(shiftId, cancellationToken);
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

    private static async Task<long?> GetOpenShiftIdAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT id_turno FROM turnos WHERE estado = 'abierto' LIMIT 1 FOR SHARE;";
        var value = await command.ExecuteScalarAsync(cancellationToken);
        return value is null or DBNull ? null : Convert.ToInt64(value);
    }

    private static async Task<CashShift?> GetOpenShiftAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, bool lockRow,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            SELECT t.id_turno, t.estado, t.abierto_en, t.cerrado_en,
                   t.id_usuario_apertura, u.nombre, t.monto_inicial
              FROM turnos t
              JOIN usuarios u ON u.id_usuario = t.id_usuario_apertura
             WHERE t.estado = 'abierto'
             LIMIT 1{(lockRow ? " FOR UPDATE OF t" : string.Empty)};
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadShift(reader) : null;
    }

    private static CashShift ReadShift(NpgsqlDataReader reader) => new(
        reader.GetInt64(0), reader.GetString(1), reader.GetFieldValue<DateTimeOffset>(2),
        reader.IsDBNull(3) ? null : reader.GetFieldValue<DateTimeOffset>(3),
        reader.GetInt64(4), reader.GetString(5), reader.GetDecimal(6));

    private static async Task<CashShiftReport?> BuildShiftReportAsync(
        NpgsqlConnection connection, NpgsqlTransaction? transaction, long shiftId,
        CancellationToken cancellationToken)
    {
        CashShift shift;
        decimal? countedCash;
        decimal? cashDifference;
        await using (var shiftCommand = connection.CreateCommand())
        {
            shiftCommand.Transaction = transaction;
            shiftCommand.CommandText = """
                SELECT t.id_turno, t.estado, t.abierto_en, t.cerrado_en,
                       t.id_usuario_apertura, u.nombre, t.monto_inicial,
                       t.efectivo_contado, t.diferencia_efectivo
                  FROM turnos t
                  JOIN usuarios u ON u.id_usuario = t.id_usuario_apertura
                 WHERE t.id_turno = @shift_id;
                """;
            shiftCommand.Parameters.AddWithValue("shift_id", shiftId);
            await using var reader = await shiftCommand.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken)) return null;
            shift = ReadShift(reader);
            countedCash = reader.IsDBNull(7) ? null : reader.GetDecimal(7);
            cashDifference = reader.IsDBNull(8) ? null : reader.GetDecimal(8);
        }

        int orderCount;
        decimal totalSales;
        decimal cashSales;
        decimal cardSales;
        decimal edenredSales;
        decimal otherSales;
        await using (var totalsCommand = connection.CreateCommand())
        {
            totalsCommand.Transaction = transaction;
            totalsCommand.CommandText = """
                WITH por_pedido AS (
                    SELECT o.id_pedido, COALESCE(o.total, 0) AS total,
                           COALESCE(SUM(p.monto) FILTER (WHERE p.estado = 'aprobado'), 0) AS pagado,
                           COALESCE(SUM(p.monto) FILTER (WHERE p.estado = 'aprobado' AND p.metodo = 'efectivo'), 0) AS efectivo,
                           COALESCE(SUM(p.monto) FILTER (WHERE p.estado = 'aprobado' AND p.metodo = 'tarjeta'), 0) AS tarjeta,
                           COALESCE(SUM(p.monto) FILTER (WHERE p.estado = 'aprobado' AND p.metodo = 'edenred'), 0) AS edenred,
                           COALESCE(SUM(p.monto) FILTER (WHERE p.estado = 'aprobado' AND p.metodo NOT IN ('efectivo','tarjeta','edenred')), 0) AS otros
                      FROM pedidos o
                      LEFT JOIN pagos p ON p.id_pedido = o.id_pedido
                     WHERE o.id_turno = @shift_id AND o.estado <> 'cancelado'
                     GROUP BY o.id_pedido, o.total
                )
                SELECT COUNT(*)::int,
                       COALESCE(SUM(total), 0),
                       COALESCE(SUM(efectivo - GREATEST(pagado - total, 0)), 0),
                       COALESCE(SUM(tarjeta), 0), COALESCE(SUM(edenred), 0), COALESCE(SUM(otros), 0)
                  FROM por_pedido;
                """;
            totalsCommand.Parameters.AddWithValue("shift_id", shiftId);
            await using var reader = await totalsCommand.ExecuteReaderAsync(cancellationToken);
            await reader.ReadAsync(cancellationToken);
            orderCount = reader.GetInt32(0);
            totalSales = reader.GetDecimal(1);
            cashSales = reader.GetDecimal(2);
            cardSales = reader.GetDecimal(3);
            edenredSales = reader.GetDecimal(4);
            otherSales = reader.GetDecimal(5);
        }

        var movements = new List<CashMovement>();
        await using (var movementsCommand = connection.CreateCommand())
        {
            movementsCommand.Transaction = transaction;
            movementsCommand.CommandText = """
                SELECT id_movimiento, tipo, monto, motivo, creado_en
                  FROM movimientos_caja WHERE id_turno = @shift_id ORDER BY creado_en;
                """;
            movementsCommand.Parameters.AddWithValue("shift_id", shiftId);
            await using var reader = await movementsCommand.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
                movements.Add(new CashMovement(reader.GetInt64(0), reader.GetString(1), reader.GetDecimal(2), reader.GetString(3), reader.GetFieldValue<DateTimeOffset>(4)));
        }

        var orders = new List<ShiftOrderReport>();
        await using (var ordersCommand = connection.CreateCommand())
        {
            ordersCommand.Transaction = transaction;
            ordersCommand.CommandText = """
                SELECT id_pedido, tipo_entrega, COALESCE(total, 0), creado_en
                  FROM pedidos WHERE id_turno = @shift_id AND estado <> 'cancelado' ORDER BY creado_en;
                """;
            ordersCommand.Parameters.AddWithValue("shift_id", shiftId);
            await using var reader = await ordersCommand.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
                orders.Add(new ShiftOrderReport(reader.GetInt64(0), reader.GetString(1), reader.GetDecimal(2), reader.GetFieldValue<DateTimeOffset>(3)));
        }

        var income = movements.Where(item => item.Type == "ingreso").Sum(item => item.Amount);
        var withdrawals = movements.Where(item => item.Type == "retiro").Sum(item => item.Amount);
        var expenses = movements.Where(item => item.Type == "gasto").Sum(item => item.Amount);
        var expectedCash = shift.OpeningAmount + cashSales + income - withdrawals - expenses;
        return new CashShiftReport(shift, orderCount, totalSales, cashSales, cardSales, edenredSales, otherSales,
            income, withdrawals, expenses, expectedCash, countedCash, cashDifference, orders, movements);
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

    private static async Task<bool> CustomerExistsAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, long customerId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT EXISTS (SELECT 1 FROM clientes WHERE id_cliente = @id);";
        command.Parameters.AddWithValue("id", customerId);
        return Convert.ToBoolean(await command.ExecuteScalarAsync(cancellationToken));
    }

    private static async Task<LoyaltyAccount?> LockLoyaltyAccountAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, long customerId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT id_cuenta, saldo_puntos
              FROM cuentas_fidelizacion
             WHERE id_cliente = @customer_id AND estado = 'activa'
             FOR UPDATE;
            """;
        command.Parameters.AddWithValue("customer_id", customerId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new LoyaltyAccount(reader.GetInt64(0), reader.GetInt32(1))
            : null;
    }

    private static async Task<ValidatedReward> ValidateRewardAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, CreateRewardItem item,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT pc.id_producto_canje, p.id_producto, p.nombre_producto, pc.coste_puntos,
                   pc.stock_disponible, pc.limite_por_pedido, p.requiere_configuracion
              FROM productos_canje pc
              JOIN productos p ON p.id_producto = pc.id_producto
             WHERE pc.id_producto_canje = @reward_id
               AND pc.activo = TRUE
               AND pc.vigente_desde <= NOW()
               AND (pc.vigente_hasta IS NULL OR pc.vigente_hasta > NOW())
               AND p.activo = TRUE AND p.disponible = TRUE
             FOR UPDATE OF pc;
            """;
        command.Parameters.AddWithValue("reward_id", item.RewardProductId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) throw new ArgumentException("Uno de los productos canjeables ya no está disponible.");
        if (reader.GetBoolean(6)) throw new ArgumentException("Los productos configurables todavía no pueden canjearse por puntos.");
        int? stock = reader.IsDBNull(4) ? null : reader.GetInt32(4);
        int? limit = reader.IsDBNull(5) ? null : reader.GetInt32(5);
        if (stock is not null && item.Quantity > stock) throw new ArgumentException("No hay stock suficiente para uno de los canjes.");
        if (limit is not null && item.Quantity > limit) throw new ArgumentException("Se superó el límite por pedido de uno de los canjes.");
        return new ValidatedReward(
            reader.GetInt64(0), reader.GetInt64(1), reader.GetString(2),
            reader.GetInt32(3), item.Quantity);
    }

    private static CatalogProduct ToCatalogProduct(ProductBuilder product) =>
        new(product.Id, product.Name, product.Category, product.Description, product.Price,
            product.Available, product.Options, product.Wrappers, product.Sauces, product.Selections);

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
        public List<CatalogRollOption> Options { get; } = [];
        public List<CatalogWrapper> Wrappers { get; } = [];
        public List<CatalogSauce> Sauces { get; } = [];
        public List<CatalogSelection> Selections { get; } = [];
    }

    private sealed record OptionRow(long Id, short Number, string Name, string Ingredients, long? FixedWrapperId);
    private sealed record WrapperRow(long Id, string Name, decimal PriceAdjustment, bool IsDefault);
    private sealed record ValidatedItem(
        long ProductId, string Name, int Quantity, decimal UnitPrice, bool RequiresConfiguration,
        long? OptionId, long? ProductWrapperId, long? SauceId, short? OptionNumber,
        string? Ingredients, string? WrapperName, string? SauceName, string? Details = null);
    private sealed record KitchenBuilder(long Id, string DeliveryType, DateTimeOffset CreatedAt)
    {
        public List<string> Products { get; } = [];
    }
    private sealed record NormalizedPayment(string Method, decimal Amount);
    private sealed record LoyaltyAccount(long AccountId, int Points);
    private sealed record ValidatedReward(long RewardProductId, long ProductId, string Name, int PointsCost, int Quantity);
    private sealed record PosOrderHeader(
        long Id, string Status, string DeliveryType, string Channel,
        decimal Subtotal, decimal Discount, decimal ShippingCost, decimal Total,
        string? Notes, string? CustomerName, string? CustomerPhone, DateTimeOffset CreatedAt);
}
