using KameronSushi.Application.Abstractions;
using KameronSushi.Application.Admin;
using Npgsql;

namespace KameronSushi.Infrastructure.Persistence;

public sealed class PostgresAdminStore(NpgsqlDataSource dataSource) : IAdminStore
{
    public async Task<IReadOnlyList<AdminCategory>> GetCategoriesAsync(CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand("""
            SELECT id_categoria, nombre_categoria, descripcion_categoria, activa, orden
              FROM categorias
             ORDER BY orden, nombre_categoria;
            """);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var result = new List<AdminCategory>();
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(ReadCategory(reader));
        }
        return result;
    }

    public async Task<AdminCategory> SaveCategoryAsync(long? categoryId, SaveAdminCategory category, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(category.Name)) throw new ArgumentException("El nombre de la categoría es obligatorio.");
        if (category.Order < 0) throw new ArgumentException("El orden no puede ser negativo.");
        await using var command = dataSource.CreateCommand(categoryId is null ? """
            INSERT INTO categorias (nombre_categoria, descripcion_categoria, activa, orden)
            VALUES (@name, @description, @active, @order) RETURNING id_categoria;
            """ : """
            UPDATE categorias SET nombre_categoria=@name, descripcion_categoria=@description, activa=@active, orden=@order
             WHERE id_categoria=@id RETURNING id_categoria;
            """);
        command.Parameters.AddWithValue("name", category.Name.Trim());
        command.Parameters.Add("description", NpgsqlTypes.NpgsqlDbType.Text).Value = string.IsNullOrWhiteSpace(category.Description) ? DBNull.Value : category.Description.Trim();
        command.Parameters.AddWithValue("active", category.Active);
        command.Parameters.AddWithValue("order", category.Order);
        if (categoryId is not null) command.Parameters.AddWithValue("id", categoryId.Value);
        try
        {
            var id = await command.ExecuteScalarAsync(cancellationToken) ?? throw new ArgumentException("La categoría no existe.");
            return (await GetCategoryAsync(Convert.ToInt64(id), cancellationToken))!;
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.UniqueViolation)
        { throw new ArgumentException("Ya existe una categoría con ese nombre."); }
    }

    public async Task<IReadOnlyList<AdminProduct>> GetProductsAsync(CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand("""
            SELECT p.id_producto, p.id_categoria, c.nombre_categoria, p.nombre_producto,
                   p.descripcion, p.precio, p.activo, p.disponible, p.requiere_configuracion, p.orden
              FROM productos p
              JOIN categorias c ON c.id_categoria = p.id_categoria
             ORDER BY c.orden, p.orden, p.nombre_producto;
            """);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var result = new List<AdminProduct>();
        while (await reader.ReadAsync(cancellationToken)) result.Add(ReadProduct(reader));
        return result;
    }

    public async Task<AdminProduct> CreateProductAsync(SaveAdminProduct product, CancellationToken cancellationToken)
    {
        ValidateProduct(product);
        await using var command = dataSource.CreateCommand("""
            INSERT INTO productos
                (id_categoria, nombre_producto, descripcion, precio, activo, disponible, requiere_configuracion, orden)
            SELECT @category_id, @name, @description, @price, @active, @available, @requires_configuration, @order
             WHERE EXISTS (SELECT 1 FROM categorias WHERE id_categoria = @category_id AND activa = TRUE)
            RETURNING id_producto;
            """);
        AddProductParameters(command, product);
        var id = await command.ExecuteScalarAsync(cancellationToken);
        if (id is null) throw new ArgumentException("La categoría seleccionada no existe o está desactivada.");
        return (await GetProductAsync(Convert.ToInt64(id), cancellationToken))!;
    }

    public async Task<AdminProduct?> UpdateProductAsync(long productId, SaveAdminProduct product, CancellationToken cancellationToken)
    {
        ValidateProduct(product);
        await using var command = dataSource.CreateCommand("""
            UPDATE productos
               SET id_categoria = @category_id,
                   nombre_producto = @name,
                   descripcion = @description,
                   precio = @price,
                   activo = @active,
                   disponible = @available,
                   requiere_configuracion = @requires_configuration,
                   orden = @order,
                   actualizado_en = NOW()
             WHERE id_producto = @product_id
               AND EXISTS (SELECT 1 FROM categorias WHERE id_categoria = @category_id)
            RETURNING id_producto;
            """);
        AddProductParameters(command, product);
        command.Parameters.AddWithValue("product_id", productId);
        var id = await command.ExecuteScalarAsync(cancellationToken);
        return id is null ? null : await GetProductAsync(productId, cancellationToken);
    }

    public async Task<IReadOnlyList<AdminRewardProduct>> GetRewardsAsync(CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand("""
            SELECT pc.id_producto_canje, pc.id_producto, p.nombre_producto, pc.coste_puntos,
                   pc.activo, pc.stock_disponible, pc.limite_por_pedido
              FROM productos_canje pc
              JOIN productos p ON p.id_producto = pc.id_producto
             ORDER BY p.nombre_producto;
            """);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var result = new List<AdminRewardProduct>();
        while (await reader.ReadAsync(cancellationToken)) result.Add(ReadReward(reader));
        return result;
    }

    public async Task<IReadOnlyList<AdminPromotion>> GetPromotionsAsync(CancellationToken cancellationToken)
    {
        await using var command=dataSource.CreateCommand("""
            SELECT a.id_promocion,a.id_producto,p.nombre_producto,p.precio,a.productos_incluidos,a.salsas_incluidas,
                   a.vigente_desde,a.vigente_hasta,a.habilitada_retiro,a.habilitada_delivery,a.activa
              FROM promociones_admin a JOIN productos p ON p.id_producto=a.id_producto ORDER BY p.nombre_producto;
            """);await using var reader=await command.ExecuteReaderAsync(cancellationToken);var result=new List<AdminPromotion>();while(await reader.ReadAsync(cancellationToken))result.Add(ReadPromotion(reader));return result;
    }

    public async Task<IReadOnlyList<AdminCustomer>> GetCustomersAsync(string? search,CancellationToken cancellationToken)
    {
        await using var command=dataSource.CreateCommand("""
            SELECT c.id_cliente,c.nombre,c.telefono,c.email,COALESCE(cf.saldo_puntos,0),COALESCE(cf.puntos_acumulados,0),COALESCE(cf.puntos_canjeados,0),COALESCE(cf.estado,'activa')
              FROM clientes c LEFT JOIN cuentas_fidelizacion cf ON cf.id_cliente=c.id_cliente
             WHERE @search='' OR c.nombre ILIKE '%'||@search||'%' OR c.telefono ILIKE '%'||@search||'%'
             ORDER BY c.nombre LIMIT 200;
            """);command.Parameters.AddWithValue("search",search?.Trim()??string.Empty);await using var reader=await command.ExecuteReaderAsync(cancellationToken);var result=new List<AdminCustomer>();while(await reader.ReadAsync(cancellationToken))result.Add(ReadCustomer(reader));return result;
    }
    public async Task<IReadOnlyList<AdminPointMovement>> GetCustomerPointMovementsAsync(long customerId,CancellationToken cancellationToken)
    {
        await using var command=dataSource.CreateCommand("""
            SELECT m.id_movimiento,m.tipo,m.puntos,m.saldo_anterior,m.saldo_resultante,m.descripcion,m.creado_en
              FROM movimiento_puntos m JOIN cuentas_fidelizacion cf ON cf.id_cuenta=m.id_cuenta
             WHERE cf.id_cliente=@customer ORDER BY m.creado_en DESC LIMIT 200;
            """);command.Parameters.AddWithValue("customer",customerId);await using var reader=await command.ExecuteReaderAsync(cancellationToken);var result=new List<AdminPointMovement>();while(await reader.ReadAsync(cancellationToken))result.Add(new(reader.GetInt64(0),reader.GetString(1),reader.GetInt32(2),reader.GetInt32(3),reader.GetInt32(4),reader.IsDBNull(5)?null:reader.GetString(5),reader.GetFieldValue<DateTimeOffset>(6)));return result;
    }
    public async Task<AdminCustomer?> AdjustCustomerPointsAsync(long customerId,AdjustCustomerPoints adjustment,CancellationToken cancellationToken)
    {
        if(adjustment.Points==0)throw new ArgumentException("El ajuste no puede ser cero.");if(string.IsNullOrWhiteSpace(adjustment.Reason))throw new ArgumentException("Escribe el motivo del ajuste.");
        await using var connection=await dataSource.OpenConnectionAsync(cancellationToken);await using var transaction=await connection.BeginTransactionAsync(cancellationToken);
        await using(var ensure=connection.CreateCommand()){ensure.Transaction=transaction;ensure.CommandText="INSERT INTO cuentas_fidelizacion(id_cliente) SELECT @customer WHERE EXISTS(SELECT 1 FROM clientes WHERE id_cliente=@customer) ON CONFLICT(id_cliente) DO NOTHING;";ensure.Parameters.AddWithValue("customer",customerId);await ensure.ExecuteNonQueryAsync(cancellationToken);}
        long account;int previous;
        await using(var lockCommand=connection.CreateCommand()){lockCommand.Transaction=transaction;lockCommand.CommandText="SELECT id_cuenta,saldo_puntos FROM cuentas_fidelizacion WHERE id_cliente=@customer FOR UPDATE;";lockCommand.Parameters.AddWithValue("customer",customerId);await using var reader=await lockCommand.ExecuteReaderAsync(cancellationToken);if(!await reader.ReadAsync(cancellationToken))return null;account=reader.GetInt64(0);previous=reader.GetInt32(1);}
        var resulting=previous+adjustment.Points;if(resulting<0)throw new ArgumentException("El ajuste dejaría el saldo de puntos negativo.");
        await using(var update=connection.CreateCommand()){update.Transaction=transaction;update.CommandText="UPDATE cuentas_fidelizacion SET saldo_puntos=@balance,actualizado_en=NOW() WHERE id_cuenta=@account; INSERT INTO movimiento_puntos(id_cuenta,tipo,puntos,saldo_anterior,saldo_resultante,descripcion) VALUES(@account,'ajuste',@points,@previous,@balance,@reason);";update.Parameters.AddWithValue("balance",resulting);update.Parameters.AddWithValue("account",account);update.Parameters.AddWithValue("points",adjustment.Points);update.Parameters.AddWithValue("previous",previous);update.Parameters.AddWithValue("reason",adjustment.Reason.Trim());await update.ExecuteNonQueryAsync(cancellationToken);}
        await transaction.CommitAsync(cancellationToken);return (await GetCustomersAsync(null,cancellationToken)).First(value=>value.CustomerId==customerId);
    }
    private static AdminCustomer ReadCustomer(NpgsqlDataReader reader)=>new(reader.GetInt64(0),reader.GetString(1),reader.GetString(2),reader.IsDBNull(3)?null:reader.GetString(3),reader.GetInt32(4),reader.GetInt32(5),reader.GetInt32(6),reader.GetString(7));

    public async Task<IReadOnlyList<AdminShiftSummary>> GetShiftsAsync(int limit,CancellationToken cancellationToken)
    {
        await using var command=dataSource.CreateCommand("""
            SELECT t.id_turno,t.estado,t.abierto_en,t.cerrado_en,u.nombre,
                   (SELECT COUNT(*) FROM pedidos p WHERE p.id_turno=t.id_turno AND p.estado<>'cancelado'),
                   (SELECT COALESCE(SUM(p.total),0) FROM pedidos p WHERE p.id_turno=t.id_turno AND p.estado<>'cancelado'),
                   t.diferencia_efectivo
              FROM turnos t JOIN usuarios u ON u.id_usuario=t.id_usuario_apertura
             ORDER BY t.abierto_en DESC LIMIT @limit;
            """);command.Parameters.AddWithValue("limit",limit);await using var reader=await command.ExecuteReaderAsync(cancellationToken);var result=new List<AdminShiftSummary>();while(await reader.ReadAsync(cancellationToken))result.Add(new(reader.GetInt64(0),reader.GetString(1),reader.GetFieldValue<DateTimeOffset>(2),reader.IsDBNull(3)?null:reader.GetFieldValue<DateTimeOffset>(3),reader.GetString(4),Convert.ToInt32(reader.GetInt64(5)),reader.GetDecimal(6),reader.IsDBNull(7)?null:reader.GetDecimal(7)));return result;
    }

    public async Task<AdminKitchenDashboard> GetKitchenDashboardAsync(long? shiftId,CancellationToken cancellationToken)
    {
        await using var connection=await dataSource.OpenConnectionAsync(cancellationToken);int target;
        await using(var setting=connection.CreateCommand()){setting.CommandText="SELECT valor::integer FROM configuracion_operativa WHERE clave='tiempo_objetivo_cocina_minutos';";target=Convert.ToInt32(await setting.ExecuteScalarAsync(cancellationToken)??20);}
        if(shiftId is null){await using var current=connection.CreateCommand();current.CommandText="SELECT id_turno FROM turnos WHERE estado='abierto' ORDER BY abierto_en DESC LIMIT 1;";var value=await current.ExecuteScalarAsync(cancellationToken);shiftId=value is null?null:Convert.ToInt64(value);}
        var metrics=new List<AdminKitchenMetric>();var products=new List<AdminKitchenProductMetric>();if(shiftId is not null)
        {
            await using(var command=connection.CreateCommand()){command.CommandText="""
                SELECT tipo_entrega,
                       COUNT(*) FILTER(WHERE estado='en_preparacion'),
                       COUNT(*) FILTER(WHERE listo_en IS NOT NULL),
                       AVG(EXTRACT(EPOCH FROM (listo_en-creado_en))/60.0) FILTER(WHERE listo_en IS NOT NULL),
                       MAX(EXTRACT(EPOCH FROM (listo_en-creado_en))/60.0) FILTER(WHERE listo_en IS NOT NULL),
                       COUNT(*) FILTER(WHERE listo_en IS NOT NULL AND listo_en-creado_en > make_interval(mins=>@target))
                  FROM pedidos WHERE id_turno=@shift AND estado<>'cancelado' GROUP BY tipo_entrega ORDER BY tipo_entrega;
                """;command.Parameters.AddWithValue("shift",shiftId.Value);command.Parameters.AddWithValue("target",target);await using var reader=await command.ExecuteReaderAsync(cancellationToken);while(await reader.ReadAsync(cancellationToken))metrics.Add(new(reader.GetString(0),Convert.ToInt32(reader.GetInt64(1)),Convert.ToInt32(reader.GetInt64(2)),reader.IsDBNull(3)?null:reader.GetDouble(3),reader.IsDBNull(4)?null:reader.GetDouble(4),Convert.ToInt32(reader.GetInt64(5))));}
            await using(var command=connection.CreateCommand()){command.CommandText="""
                SELECT d.nombre_producto,COUNT(DISTINCT p.id_pedido),AVG(EXTRACT(EPOCH FROM(p.listo_en-p.creado_en))/60.0)
                  FROM pedidos p JOIN detalle_pedidos d ON d.id_pedido=p.id_pedido
                 WHERE p.id_turno=@shift AND p.listo_en IS NOT NULL GROUP BY d.nombre_producto ORDER BY 3 DESC LIMIT 10;
                """;command.Parameters.AddWithValue("shift",shiftId.Value);await using var reader=await command.ExecuteReaderAsync(cancellationToken);while(await reader.ReadAsync(cancellationToken))products.Add(new(reader.GetString(0),Convert.ToInt32(reader.GetInt64(1)),reader.GetDouble(2)));}
        }
        return new(shiftId,target,metrics,products);
    }
    public async Task<int> SaveKitchenTargetAsync(int targetMinutes,CancellationToken cancellationToken){if(targetMinutes is<1 or>240)throw new ArgumentException("El objetivo debe estar entre 1 y 240 minutos.");await using var command=dataSource.CreateCommand("INSERT INTO configuracion_operativa(clave,valor) VALUES('tiempo_objetivo_cocina_minutos',@value) ON CONFLICT(clave) DO UPDATE SET valor=EXCLUDED.valor,actualizado_en=NOW();");command.Parameters.AddWithValue("value",targetMinutes.ToString(System.Globalization.CultureInfo.InvariantCulture));await command.ExecuteNonQueryAsync(cancellationToken);return targetMinutes;}

    public async Task<AdminPromotion> SavePromotionAsync(long? promotionId,SaveAdminPromotion promotion,CancellationToken cancellationToken)
    {
        if(promotion.ProductId<=0)throw new ArgumentException("Selecciona el producto de la promoción.");if(promotion.IncludedSauces<0)throw new ArgumentException("La cantidad de salsas no puede ser negativa.");if(!promotion.PickupEnabled&&!promotion.DeliveryEnabled)throw new ArgumentException("Habilita al menos un tipo de entrega.");if(promotion.StartsAt is not null&&promotion.EndsAt<=promotion.StartsAt)throw new ArgumentException("El término debe ser posterior al inicio.");
        await using var command=dataSource.CreateCommand(promotionId is null?"""
            INSERT INTO promociones_admin(id_producto,productos_incluidos,salsas_incluidas,vigente_desde,vigente_hasta,habilitada_retiro,habilitada_delivery,activa)
            VALUES(@product,@included,@sauces,@starts,@ends,@pickup,@delivery,@active) RETURNING id_promocion;
            """:"""
            UPDATE promociones_admin SET id_producto=@product,productos_incluidos=@included,salsas_incluidas=@sauces,vigente_desde=@starts,vigente_hasta=@ends,habilitada_retiro=@pickup,habilitada_delivery=@delivery,activa=@active,actualizado_en=NOW()
             WHERE id_promocion=@id RETURNING id_promocion;
            """);command.Parameters.AddWithValue("product",promotion.ProductId);command.Parameters.Add("included",NpgsqlTypes.NpgsqlDbType.Text).Value=string.IsNullOrWhiteSpace(promotion.IncludedProducts)?DBNull.Value:promotion.IncludedProducts.Trim();command.Parameters.AddWithValue("sauces",promotion.IncludedSauces);command.Parameters.Add("starts",NpgsqlTypes.NpgsqlDbType.TimestampTz).Value=(object?)promotion.StartsAt??DBNull.Value;command.Parameters.Add("ends",NpgsqlTypes.NpgsqlDbType.TimestampTz).Value=(object?)promotion.EndsAt??DBNull.Value;command.Parameters.AddWithValue("pickup",promotion.PickupEnabled);command.Parameters.AddWithValue("delivery",promotion.DeliveryEnabled);command.Parameters.AddWithValue("active",promotion.Active);if(promotionId is not null)command.Parameters.AddWithValue("id",promotionId.Value);
        try{var id=Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken)??throw new ArgumentException("La promoción no existe."));return (await GetPromotionAsync(id,cancellationToken))!;}catch(PostgresException exception)when(exception.SqlState==PostgresErrorCodes.UniqueViolation){throw new ArgumentException("Ese producto ya tiene una promoción configurada.");}
    }

    private async Task<AdminPromotion?> GetPromotionAsync(long id,CancellationToken token){await using var command=dataSource.CreateCommand("SELECT a.id_promocion,a.id_producto,p.nombre_producto,p.precio,a.productos_incluidos,a.salsas_incluidas,a.vigente_desde,a.vigente_hasta,a.habilitada_retiro,a.habilitada_delivery,a.activa FROM promociones_admin a JOIN productos p ON p.id_producto=a.id_producto WHERE a.id_promocion=@id;");command.Parameters.AddWithValue("id",id);await using var reader=await command.ExecuteReaderAsync(token);return await reader.ReadAsync(token)?ReadPromotion(reader):null;}
    private static AdminPromotion ReadPromotion(NpgsqlDataReader reader)=>new(reader.GetInt64(0),reader.GetInt64(1),reader.GetString(2),reader.GetDecimal(3),reader.IsDBNull(4)?null:reader.GetString(4),reader.GetInt32(5),reader.IsDBNull(6)?null:reader.GetFieldValue<DateTimeOffset>(6),reader.IsDBNull(7)?null:reader.GetFieldValue<DateTimeOffset>(7),reader.GetBoolean(8),reader.GetBoolean(9),reader.GetBoolean(10));

    public async Task<IReadOnlyList<AdminWrapper>> GetWrappersAsync(CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand("SELECT id_envoltura, nombre, activa FROM envolturas ORDER BY nombre;");
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var result = new List<AdminWrapper>();
        while (await reader.ReadAsync(cancellationToken)) result.Add(new(reader.GetInt64(0), reader.GetString(1), reader.GetBoolean(2)));
        return result;
    }

    public async Task<AdminWrapper> SaveWrapperAsync(long? wrapperId, SaveAdminWrapper wrapper, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(wrapper.Name)) throw new ArgumentException("El nombre de la envoltura es obligatorio.");
        await using var command = dataSource.CreateCommand(wrapperId is null
            ? "INSERT INTO envolturas (nombre, activa) VALUES (@name,@active) RETURNING id_envoltura;"
            : "UPDATE envolturas SET nombre=@name, activa=@active WHERE id_envoltura=@id RETURNING id_envoltura;");
        command.Parameters.AddWithValue("name", wrapper.Name.Trim()); command.Parameters.AddWithValue("active", wrapper.Active);
        if (wrapperId is not null) command.Parameters.AddWithValue("id", wrapperId.Value);
        try
        {
            var id = Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken) ?? throw new ArgumentException("La envoltura no existe."));
            return new AdminWrapper(id, wrapper.Name.Trim(), wrapper.Active);
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.UniqueViolation)
        { throw new ArgumentException("Ya existe una envoltura con ese nombre."); }
    }

    public async Task<IReadOnlyList<AdminSauce>> GetSaucesAsync(CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand("SELECT id_salsa,nombre,descripcion,activa FROM salsas ORDER BY nombre;");
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var result = new List<AdminSauce>();
        while (await reader.ReadAsync(cancellationToken)) result.Add(new(reader.GetInt64(0), reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetString(2), reader.GetBoolean(3)));
        return result;
    }

    public async Task<AdminSauce> SaveSauceAsync(long? sauceId, SaveAdminSauce sauce, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(sauce.Name)) throw new ArgumentException("El nombre de la salsa es obligatorio.");
        await using var command = dataSource.CreateCommand(sauceId is null ? """
            INSERT INTO salsas (nombre,descripcion,activa) VALUES (@name,@description,@active) RETURNING id_salsa;
            """ : """
            UPDATE salsas SET nombre=@name,descripcion=@description,activa=@active WHERE id_salsa=@id RETURNING id_salsa;
            """);
        command.Parameters.AddWithValue("name", sauce.Name.Trim());
        command.Parameters.Add("description", NpgsqlTypes.NpgsqlDbType.Text).Value = string.IsNullOrWhiteSpace(sauce.Description) ? DBNull.Value : sauce.Description.Trim();
        command.Parameters.AddWithValue("active", sauce.Active); if (sauceId is not null) command.Parameters.AddWithValue("id", sauceId.Value);
        try
        {
            var id = Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken) ?? throw new ArgumentException("La salsa no existe."));
            return new AdminSauce(id, sauce.Name.Trim(), sauce.Description?.Trim(), sauce.Active);
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.UniqueViolation)
        { throw new ArgumentException("Ya existe una salsa con ese nombre."); }
    }

    public async Task<AdminProductConfiguration?> GetProductConfigurationAsync(long productId, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var exists = connection.CreateCommand(); exists.CommandText = "SELECT EXISTS(SELECT 1 FROM productos WHERE id_producto=@id);"; exists.Parameters.AddWithValue("id", productId);
        if (!Convert.ToBoolean(await exists.ExecuteScalarAsync(cancellationToken))) return null;
        var wrappers = new List<AdminProductWrapper>();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT pe.id_producto_envoltura,e.id_envoltura,e.nombre,pe.precio_adicional,pe.predeterminada,pe.activa
                  FROM producto_envolturas pe JOIN envolturas e ON e.id_envoltura=pe.id_envoltura
                 WHERE pe.id_producto=@id ORDER BY pe.predeterminada DESC,e.nombre;
                """; command.Parameters.AddWithValue("id", productId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken)) wrappers.Add(new(reader.GetInt64(0),reader.GetInt64(1),reader.GetString(2),reader.GetDecimal(3),reader.GetBoolean(4),reader.GetBoolean(5)));
        }
        var options = new List<AdminRollOption>();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT id_opcion_roll,numero_opcion,nombre,ingredientes,id_producto_envoltura_fija,activa FROM opciones_roll WHERE id_producto=@id ORDER BY numero_opcion;"; command.Parameters.AddWithValue("id", productId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken)) options.Add(new(reader.GetInt64(0),reader.GetInt16(1),reader.GetString(2),reader.GetString(3),reader.IsDBNull(4)?null:reader.GetInt64(4),reader.GetBoolean(5)));
        }
        return new(productId, wrappers, options);
    }

    public async Task<AdminProductConfiguration?> SaveProductConfigurationAsync(long productId, SaveAdminProductConfiguration configuration, CancellationToken cancellationToken)
    {
        if (configuration.Wrappers.Count == 0) throw new ArgumentException("Un producto configurable debe tener al menos una envoltura.");
        if (configuration.Options.Count == 0) throw new ArgumentException("Un producto configurable debe tener al menos una opción de ingredientes.");
        if (configuration.Wrappers.Count(value => value.Active && value.IsDefault) > 1) throw new ArgumentException("Solo una envoltura puede ser predeterminada.");
        if (configuration.Options.GroupBy(value => value.Number).Any(group => group.Count() > 1)) throw new ArgumentException("Los números de opción no pueden repetirse.");
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using (var updateProduct = connection.CreateCommand())
        {
            updateProduct.Transaction=transaction; updateProduct.CommandText="UPDATE productos SET requiere_configuracion=TRUE,actualizado_en=NOW() WHERE id_producto=@id;"; updateProduct.Parameters.AddWithValue("id",productId);
            if (await updateProduct.ExecuteNonQueryAsync(cancellationToken)==0) return null;
        }
        var wrapperIds = new Dictionary<long,long>();
        foreach (var wrapper in configuration.Wrappers)
        {
            if (wrapper.PriceAdjustment < 0) throw new ArgumentException("El recargo no puede ser negativo.");
            await using var command=connection.CreateCommand(); command.Transaction=transaction;
            command.CommandText = wrapper.ProductWrapperId is null ? """
                INSERT INTO producto_envolturas(id_producto,id_envoltura,precio_adicional,predeterminada,activa)
                VALUES(@product,@wrapper,@price,@default,@active) RETURNING id_producto_envoltura;
                """ : """
                UPDATE producto_envolturas SET id_envoltura=@wrapper,precio_adicional=@price,predeterminada=@default,activa=@active
                 WHERE id_producto_envoltura=@association AND id_producto=@product RETURNING id_producto_envoltura;
                """;
            command.Parameters.AddWithValue("product",productId);command.Parameters.AddWithValue("wrapper",wrapper.WrapperId);command.Parameters.AddWithValue("price",wrapper.PriceAdjustment);command.Parameters.AddWithValue("default",wrapper.IsDefault);command.Parameters.AddWithValue("active",wrapper.Active);
            if(wrapper.ProductWrapperId is not null)command.Parameters.AddWithValue("association",wrapper.ProductWrapperId.Value);
            var associationId=Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken)??throw new ArgumentException("Una envoltura configurada no existe.")); wrapperIds[wrapper.WrapperId]=associationId;
        }
        foreach(var option in configuration.Options)
        {
            if(option.Number<=0||string.IsNullOrWhiteSpace(option.Name)||string.IsNullOrWhiteSpace(option.Ingredients))throw new ArgumentException("Cada opción requiere número, nombre e ingredientes.");
            long? fixedAssociation=null;
            if(option.FixedWrapperId is long wrapperId && !wrapperIds.TryGetValue(wrapperId,out var association))throw new ArgumentException("La envoltura fija debe pertenecer al producto."); else if(option.FixedWrapperId is not null) fixedAssociation=wrapperIds[option.FixedWrapperId.Value];
            await using var command=connection.CreateCommand();command.Transaction=transaction;
            command.CommandText=option.OptionId is null?"""
                INSERT INTO opciones_roll(id_producto,id_producto_envoltura_fija,numero_opcion,nombre,ingredientes,activa)
                VALUES(@product,@fixed,@number,@name,@ingredients,@active);
                """:"""
                UPDATE opciones_roll SET id_producto_envoltura_fija=@fixed,numero_opcion=@number,nombre=@name,ingredientes=@ingredients,activa=@active,actualizado_en=NOW()
                 WHERE id_opcion_roll=@option AND id_producto=@product;
                """;
            command.Parameters.AddWithValue("product",productId);command.Parameters.Add("fixed",NpgsqlTypes.NpgsqlDbType.Bigint).Value=(object?)fixedAssociation??DBNull.Value;command.Parameters.AddWithValue("number",option.Number);command.Parameters.AddWithValue("name",option.Name.Trim());command.Parameters.AddWithValue("ingredients",option.Ingredients.Trim());command.Parameters.AddWithValue("active",option.Active);if(option.OptionId is not null)command.Parameters.AddWithValue("option",option.OptionId.Value);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
        return await GetProductConfigurationAsync(productId,cancellationToken);
    }

    public async Task<AdminRewardProduct> SaveRewardAsync(SaveAdminRewardProduct reward, CancellationToken cancellationToken)
    {
        if (reward.ProductId <= 0) throw new ArgumentException("Selecciona un producto.");
        if (reward.PointsCost <= 0) throw new ArgumentException("El coste debe ser mayor que cero.");
        if (reward.Stock < 0) throw new ArgumentException("El stock no puede ser negativo.");
        if (reward.LimitPerOrder <= 0) throw new ArgumentException("El límite debe ser mayor que cero.");

        await using var command = dataSource.CreateCommand("""
            INSERT INTO productos_canje
                (id_producto, coste_puntos, activo, stock_disponible, limite_por_pedido)
            SELECT @product_id, @points, @active, @stock, @limit
             WHERE EXISTS (
                 SELECT 1 FROM productos
                  WHERE id_producto = @product_id AND requiere_configuracion = FALSE
             )
            ON CONFLICT (id_producto) DO UPDATE SET
                coste_puntos = EXCLUDED.coste_puntos,
                activo = EXCLUDED.activo,
                stock_disponible = EXCLUDED.stock_disponible,
                limite_por_pedido = EXCLUDED.limite_por_pedido,
                vigente_desde = CASE WHEN productos_canje.activo = FALSE AND EXCLUDED.activo = TRUE THEN NOW() ELSE productos_canje.vigente_desde END,
                vigente_hasta = NULL,
                actualizado_en = NOW()
            RETURNING id_producto_canje;
            """);
        command.Parameters.AddWithValue("product_id", reward.ProductId);
        command.Parameters.AddWithValue("points", reward.PointsCost);
        command.Parameters.AddWithValue("active", reward.Active);
        command.Parameters.Add("stock", NpgsqlTypes.NpgsqlDbType.Integer).Value = (object?)reward.Stock ?? DBNull.Value;
        command.Parameters.Add("limit", NpgsqlTypes.NpgsqlDbType.Integer).Value = (object?)reward.LimitPerOrder ?? DBNull.Value;
        var id = await command.ExecuteScalarAsync(cancellationToken);
        if (id is null) throw new ArgumentException("Solo los productos simples pueden configurarse como canjeables.");
        return (await GetRewardAsync(Convert.ToInt64(id), cancellationToken))!;
    }

    public async Task<bool> DisableRewardAsync(long rewardProductId, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand("""
            UPDATE productos_canje
               SET activo = FALSE, vigente_hasta = NOW(), actualizado_en = NOW()
             WHERE id_producto_canje = @id AND activo = TRUE;
            """);
        command.Parameters.AddWithValue("id", rewardProductId);
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    private async Task<AdminProduct?> GetProductAsync(long id, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand("""
            SELECT p.id_producto, p.id_categoria, c.nombre_categoria, p.nombre_producto,
                   p.descripcion, p.precio, p.activo, p.disponible, p.requiere_configuracion, p.orden
              FROM productos p JOIN categorias c ON c.id_categoria = p.id_categoria
             WHERE p.id_producto = @id;
            """);
        command.Parameters.AddWithValue("id", id);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadProduct(reader) : null;
    }

    private async Task<AdminRewardProduct?> GetRewardAsync(long id, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand("""
            SELECT pc.id_producto_canje, pc.id_producto, p.nombre_producto, pc.coste_puntos,
                   pc.activo, pc.stock_disponible, pc.limite_por_pedido
              FROM productos_canje pc JOIN productos p ON p.id_producto = pc.id_producto
             WHERE pc.id_producto_canje = @id;
            """);
        command.Parameters.AddWithValue("id", id);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadReward(reader) : null;
    }

    private static AdminProduct ReadProduct(NpgsqlDataReader reader) => new(
        reader.GetInt64(0), reader.GetInt64(1), reader.GetString(2), reader.GetString(3),
        reader.IsDBNull(4) ? null : reader.GetString(4), reader.GetDecimal(5),
        reader.GetBoolean(6), reader.GetBoolean(7), reader.GetBoolean(8), reader.GetInt32(9));

    private async Task<AdminCategory?> GetCategoryAsync(long id, CancellationToken cancellationToken)
    {
        await using var command=dataSource.CreateCommand("SELECT id_categoria,nombre_categoria,descripcion_categoria,activa,orden FROM categorias WHERE id_categoria=@id;");command.Parameters.AddWithValue("id",id);await using var reader=await command.ExecuteReaderAsync(cancellationToken);return await reader.ReadAsync(cancellationToken)?ReadCategory(reader):null;
    }
    private static AdminCategory ReadCategory(NpgsqlDataReader reader)=>new(reader.GetInt64(0),reader.GetString(1),reader.IsDBNull(2)?null:reader.GetString(2),reader.GetBoolean(3),reader.GetInt32(4));

    private static AdminRewardProduct ReadReward(NpgsqlDataReader reader) => new(
        reader.GetInt64(0), reader.GetInt64(1), reader.GetString(2), reader.GetInt32(3),
        reader.GetBoolean(4), reader.IsDBNull(5) ? null : reader.GetInt32(5),
        reader.IsDBNull(6) ? null : reader.GetInt32(6));

    private static void ValidateProduct(SaveAdminProduct product)
    {
        if (product.CategoryId <= 0) throw new ArgumentException("Selecciona una categoría.");
        if (string.IsNullOrWhiteSpace(product.Name)) throw new ArgumentException("El nombre es obligatorio.");
        if (product.Name.Trim().Length > 150) throw new ArgumentException("El nombre no puede superar 150 caracteres.");
        if (product.Price < 0) throw new ArgumentException("El precio no puede ser negativo.");
    }

    private static void AddProductParameters(NpgsqlCommand command, SaveAdminProduct product)
    {
        command.Parameters.AddWithValue("category_id", product.CategoryId);
        command.Parameters.AddWithValue("name", product.Name.Trim());
        command.Parameters.Add("description", NpgsqlTypes.NpgsqlDbType.Text).Value =
            string.IsNullOrWhiteSpace(product.Description) ? DBNull.Value : product.Description.Trim();
        command.Parameters.AddWithValue("price", product.Price);
        command.Parameters.AddWithValue("active", product.Active);
        command.Parameters.AddWithValue("available", product.Available);
        command.Parameters.AddWithValue("requires_configuration", product.RequiresConfiguration);
        command.Parameters.AddWithValue("order", product.Order);
    }
}
