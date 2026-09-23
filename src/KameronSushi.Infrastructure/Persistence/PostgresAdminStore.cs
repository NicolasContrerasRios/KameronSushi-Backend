using KameronSushi.Application.Abstractions;
using KameronSushi.Application.Admin;
using Npgsql;

namespace KameronSushi.Infrastructure.Persistence;

public sealed class PostgresAdminStore(NpgsqlDataSource dataSource) : IAdminStore
{
    public async Task<IReadOnlyList<AdminCategory>> GetCategoriesAsync(CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand("""
            SELECT id_categoria, nombre_categoria, activa, orden
              FROM categorias
             ORDER BY orden, nombre_categoria;
            """);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var result = new List<AdminCategory>();
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new AdminCategory(reader.GetInt64(0), reader.GetString(1), reader.GetBoolean(2), reader.GetInt32(3)));
        }
        return result;
    }

    public async Task<IReadOnlyList<AdminProduct>> GetProductsAsync(CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand("""
            SELECT p.id_producto, p.id_categoria, c.nombre_categoria, p.nombre_producto,
                   p.descripcion, p.precio, p.activo, p.disponible, p.requiere_configuracion
              FROM productos p
              JOIN categorias c ON c.id_categoria = p.id_categoria
             ORDER BY c.orden, p.nombre_producto;
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
                (id_categoria, nombre_producto, descripcion, precio, activo, disponible, requiere_configuracion)
            SELECT @category_id, @name, @description, @price, @active, @available, FALSE
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
                   p.descripcion, p.precio, p.activo, p.disponible, p.requiere_configuracion
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
        reader.GetBoolean(6), reader.GetBoolean(7), reader.GetBoolean(8));

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
    }
}
