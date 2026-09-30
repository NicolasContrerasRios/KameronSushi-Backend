using System.Data;
using System.Security.Cryptography;
using System.Text;
using KameronSushi.Application.Abstractions;
using KameronSushi.Application.Auth;
using KameronSushi.Infrastructure.Configuration;
using Microsoft.Extensions.Options;
using Npgsql;
using NpgsqlTypes;

namespace KameronSushi.Infrastructure.Persistence;

public sealed class PostgresAuthStore(
    NpgsqlDataSource dataSource,
    IPasswordHasher passwordHasher,
    IOptions<AuthOptions> options) : IAuthStore
{
    private static readonly HashSet<string> ValidRoles = ["administrador", "caja", "cocina"];

    public async Task<UserSession?> LoginAsync(
        string email, string password, string? deviceName, CancellationToken cancellationToken)
    {
        var normalizedEmail = NormalizeEmail(email);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        long userId;
        string name;
        string storedEmail;
        string passwordHash;
        string status;

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT id_usuario,nombre,email,password_hash,estado FROM usuarios WHERE lower(email)=@email;";
            command.Parameters.AddWithValue("email", normalizedEmail);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken)) return null;
            userId = reader.GetInt64(0);
            name = reader.GetString(1);
            storedEmail = reader.GetString(2);
            passwordHash = reader.GetString(3);
            status = reader.GetString(4);
        }

        if (status != "activo" || !passwordHasher.Verify(password, passwordHash)) return null;
        var roles = await GetRolesAsync(connection, null, userId, cancellationToken);
        if (roles.Count == 0) return null;

        var tokenBytes = RandomNumberGenerator.GetBytes(32);
        var token = Convert.ToBase64String(tokenBytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var expiresAt = DateTimeOffset.UtcNow.AddHours(Math.Clamp(options.Value.SessionHours, 1, 168));
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                DELETE FROM sesiones_usuario WHERE expira_en <= NOW();
                INSERT INTO sesiones_usuario(id_usuario,token_hash,dispositivo,expira_en)
                VALUES(@user_id,@token_hash,@device,@expires_at);
                """;
            command.Parameters.AddWithValue("user_id", userId);
            command.Parameters.AddWithValue("token_hash", HashToken(token));
            command.Parameters.Add("device", NpgsqlDbType.Varchar).Value =
                string.IsNullOrWhiteSpace(deviceName) ? DBNull.Value : deviceName.Trim()[..Math.Min(deviceName.Trim().Length, 150)];
            command.Parameters.AddWithValue("expires_at", expiresAt);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        return new UserSession(token, expiresAt, new AuthenticatedUser(userId, name, storedEmail, roles));
    }

    public async Task<AuthenticatedUser?> GetSessionUserAsync(string token, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(token)) return null;
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        long userId;
        string name;
        string email;
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT u.id_usuario,u.nombre,u.email
                  FROM sesiones_usuario s
                  JOIN usuarios u ON u.id_usuario=s.id_usuario
                 WHERE s.token_hash=@token_hash AND s.revocada_en IS NULL
                   AND s.expira_en>NOW() AND u.estado='activo';
                """;
            command.Parameters.AddWithValue("token_hash", HashToken(token));
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken)) return null;
            userId = reader.GetInt64(0);
            name = reader.GetString(1);
            email = reader.GetString(2);
        }
        var roles = await GetRolesAsync(connection, null, userId, cancellationToken);
        return roles.Count == 0 ? null : new AuthenticatedUser(userId, name, email, roles);
    }

    public async Task RevokeSessionAsync(string token, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(token)) return;
        await using var command = dataSource.CreateCommand(
            "UPDATE sesiones_usuario SET revocada_en=NOW() WHERE token_hash=@token_hash AND revocada_en IS NULL;");
        command.Parameters.AddWithValue("token_hash", HashToken(token));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<bool> HasAdministratorAsync(CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand("""
            SELECT EXISTS(
                SELECT 1 FROM usuarios u
                JOIN usuario_roles ur ON ur.id_usuario=u.id_usuario
                JOIN roles r ON r.id_rol=ur.id_rol
                WHERE u.estado='activo' AND r.codigo='administrador');
            """);
        return Convert.ToBoolean(await command.ExecuteScalarAsync(cancellationToken));
    }

    public async Task<AdminUser> BootstrapAdministratorAsync(CreateAdminUser user, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        await using (var lockCommand = connection.CreateCommand())
        {
            lockCommand.Transaction = transaction;
            lockCommand.CommandText = "SELECT pg_advisory_xact_lock(hashtext('kameron_sushi_auth_bootstrap'));";
            await lockCommand.ExecuteNonQueryAsync(cancellationToken);
        }
        await using (var check = connection.CreateCommand())
        {
            check.Transaction = transaction;
            check.CommandText = "SELECT EXISTS(SELECT 1 FROM usuario_roles ur JOIN roles r ON r.id_rol=ur.id_rol WHERE r.codigo='administrador');";
            if (Convert.ToBoolean(await check.ExecuteScalarAsync(cancellationToken)))
                throw new InvalidOperationException("La configuración inicial ya fue completada.");
        }
        var created = await CreateUserInternalAsync(connection, transaction,
            user with { Roles = ["administrador"] }, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return created;
    }

    public async Task<IReadOnlyList<AdminUser>> GetUsersAsync(CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand("""
            SELECT u.id_usuario,u.nombre,u.email,u.estado,u.creado_en,
                   COALESCE(array_agg(r.codigo ORDER BY r.codigo) FILTER (WHERE r.codigo IS NOT NULL),'{}')
              FROM usuarios u
              LEFT JOIN usuario_roles ur ON ur.id_usuario=u.id_usuario
              LEFT JOIN roles r ON r.id_rol=ur.id_rol
             GROUP BY u.id_usuario
             ORDER BY u.nombre,u.email;
            """);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var result = new List<AdminUser>();
        while (await reader.ReadAsync(cancellationToken)) result.Add(ReadAdminUser(reader));
        return result;
    }

    public async Task<AdminUser> CreateUserAsync(CreateAdminUser user, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            var created = await CreateUserInternalAsync(connection, transaction, user, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return created;
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            throw new ArgumentException("Ya existe un usuario con ese correo.");
        }
    }

    public async Task<AdminUser?> UpdateUserAsync(
        long userId, UpdateAdminUser user, long actingUserId, CancellationToken cancellationToken)
    {
        ValidateIdentity(user.Name, user.Email);
        var roles = NormalizeRoles(user.Roles);
        var status = user.Status.Trim().ToLowerInvariant();
        if (status is not ("activo" or "bloqueado" or "deshabilitado"))
            throw new ArgumentException("El estado del usuario no es válido.");
        if (userId == actingUserId && (status != "activo" || !roles.Contains("administrador")))
            throw new ArgumentException("No puedes desactivar tu propia cuenta ni quitarte el rol administrador.");

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = string.IsNullOrWhiteSpace(user.NewPassword)
                ? "UPDATE usuarios SET nombre=@name,email=@email,estado=@status,actualizado_en=NOW() WHERE id_usuario=@id RETURNING id_usuario;"
                : "UPDATE usuarios SET nombre=@name,email=@email,estado=@status,password_hash=@password,actualizado_en=NOW() WHERE id_usuario=@id RETURNING id_usuario;";
            command.Parameters.AddWithValue("id", userId);
            command.Parameters.AddWithValue("name", user.Name.Trim());
            command.Parameters.AddWithValue("email", NormalizeEmail(user.Email));
            command.Parameters.AddWithValue("status", status);
            if (!string.IsNullOrWhiteSpace(user.NewPassword)) command.Parameters.AddWithValue("password", passwordHasher.Hash(user.NewPassword));
            if (await command.ExecuteScalarAsync(cancellationToken) is null) return null;
            await ReplaceRolesAsync(connection, transaction, userId, roles, cancellationToken);
            if (status != "activo" || !string.IsNullOrWhiteSpace(user.NewPassword))
                await RevokeUserSessionsAsync(connection, transaction, userId, cancellationToken);
            var updated = await GetUserAsync(connection, transaction, userId, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return updated;
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            throw new ArgumentException("Ya existe un usuario con ese correo.");
        }
    }

    private async Task<AdminUser> CreateUserInternalAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, CreateAdminUser user, CancellationToken cancellationToken)
    {
        ValidateIdentity(user.Name, user.Email);
        var roles = NormalizeRoles(user.Roles);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO usuarios(nombre,email,password_hash,estado)
            VALUES(@name,@email,@password,'activo') RETURNING id_usuario;
            """;
        command.Parameters.AddWithValue("name", user.Name.Trim());
        command.Parameters.AddWithValue("email", NormalizeEmail(user.Email));
        command.Parameters.AddWithValue("password", passwordHasher.Hash(user.Password));
        var userId = Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken));
        await ReplaceRolesAsync(connection, transaction, userId, roles, cancellationToken);
        return (await GetUserAsync(connection, transaction, userId, cancellationToken))!;
    }

    private static async Task ReplaceRolesAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, long userId,
        IReadOnlyCollection<string> roles, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            DELETE FROM usuario_roles WHERE id_usuario=@user_id;
            INSERT INTO usuario_roles(id_usuario,id_rol)
            SELECT @user_id,id_rol FROM roles WHERE codigo=ANY(@roles);
            """;
        command.Parameters.AddWithValue("user_id", userId);
        command.Parameters.AddWithValue("roles", roles.ToArray());
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task RevokeUserSessionsAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, long userId, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "UPDATE sesiones_usuario SET revocada_en=NOW() WHERE id_usuario=@id AND revocada_en IS NULL;";
        command.Parameters.AddWithValue("id", userId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<AdminUser?> GetUserAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, long userId, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT u.id_usuario,u.nombre,u.email,u.estado,u.creado_en,
                   COALESCE(array_agg(r.codigo ORDER BY r.codigo) FILTER (WHERE r.codigo IS NOT NULL),'{}')
              FROM usuarios u
              LEFT JOIN usuario_roles ur ON ur.id_usuario=u.id_usuario
              LEFT JOIN roles r ON r.id_rol=ur.id_rol
             WHERE u.id_usuario=@id GROUP BY u.id_usuario;
            """;
        command.Parameters.AddWithValue("id", userId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadAdminUser(reader) : null;
    }

    private static async Task<IReadOnlyList<string>> GetRolesAsync(
        NpgsqlConnection connection, NpgsqlTransaction? transaction, long userId, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT r.codigo FROM usuario_roles ur JOIN roles r ON r.id_rol=ur.id_rol WHERE ur.id_usuario=@id ORDER BY r.codigo;";
        command.Parameters.AddWithValue("id", userId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var roles = new List<string>();
        while (await reader.ReadAsync(cancellationToken)) roles.Add(reader.GetString(0));
        return roles;
    }

    private static AdminUser ReadAdminUser(NpgsqlDataReader reader) => new(
        reader.GetInt64(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
        reader.GetFieldValue<string[]>(5), reader.GetFieldValue<DateTimeOffset>(4));

    private static string NormalizeEmail(string email)
    {
        if (string.IsNullOrWhiteSpace(email)) throw new ArgumentException("El correo es obligatorio.");
        var normalized = email.Trim().ToLowerInvariant();
        if (normalized.Length > 254 || !normalized.Contains('@')) throw new ArgumentException("El correo no es válido.");
        return normalized;
    }

    private static void ValidateIdentity(string name, string email)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("El nombre es obligatorio.");
        if (name.Trim().Length > 150) throw new ArgumentException("El nombre no puede superar 150 caracteres.");
        NormalizeEmail(email);
    }

    private static IReadOnlyCollection<string> NormalizeRoles(IEnumerable<string> suppliedRoles)
    {
        var roles = suppliedRoles.Select(value => value.Trim().ToLowerInvariant()).Distinct().ToArray();
        if (roles.Length == 0 || roles.Any(role => !ValidRoles.Contains(role)))
            throw new ArgumentException("Selecciona al menos un rol válido: administrador, caja o cocina.");
        return roles;
    }

    private static string HashToken(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();
}
