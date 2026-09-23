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
