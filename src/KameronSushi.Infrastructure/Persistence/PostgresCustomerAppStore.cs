using System.Data;
using System.Security.Cryptography;
using System.Text;
using KameronSushi.Application.Abstractions;
using KameronSushi.Application.Customers;
using Npgsql;
using NpgsqlTypes;

namespace KameronSushi.Infrastructure.Persistence;

public sealed class PostgresCustomerAppStore(
    NpgsqlDataSource dataSource, IPasswordHasher passwordHasher) : ICustomerAppStore
{
    public async Task<CustomerSession> RegisterAsync(RegisterCustomer request, CancellationToken cancellationToken)
    {
        var name = Required(request.Name, "nombre", 150);
        var phone = NormalizePhone(request.Phone);
        var email = string.IsNullOrWhiteSpace(request.Email) ? null : request.Email.Trim().ToLowerInvariant();
        if (email?.Length > 254) throw new ArgumentException("El correo no puede superar 254 caracteres.");
        var pinHash = passwordHasher.Hash(request.Pin);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        await using (var advisory = connection.CreateCommand())
        {
            advisory.Transaction = transaction;
            advisory.CommandText = "SELECT pg_advisory_xact_lock(hashtextextended(@phone,0));";
            advisory.Parameters.AddWithValue("phone", phone);
            await advisory.ExecuteNonQueryAsync(cancellationToken);
        }
        long customerId;
        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                WITH existing AS (
                    SELECT id_cliente FROM clientes
                     WHERE RIGHT(REGEXP_REPLACE(telefono,'\D','','g'),9)=RIGHT(@phone,9)
                     ORDER BY creado_en LIMIT 1
                ), inserted AS (
                    INSERT INTO clientes(nombre,telefono,email)
                    SELECT @name,@phone,@email WHERE NOT EXISTS(SELECT 1 FROM existing)
                    RETURNING id_cliente
                )
                SELECT id_cliente FROM existing UNION ALL SELECT id_cliente FROM inserted LIMIT 1;
                """;
            command.Parameters.AddWithValue("name", name);
            command.Parameters.AddWithValue("phone", phone);
            command.Parameters.Add("email", NpgsqlDbType.Varchar).Value = (object?)email ?? DBNull.Value;
            customerId = Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken));
        }
        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO cuentas_cliente_app(id_cliente,telefono_normalizado,pin_hash)
                VALUES(@customer,@phone,@pin)
                ON CONFLICT(telefono_normalizado) DO NOTHING;
                """;
            command.Parameters.AddWithValue("customer", customerId);
            command.Parameters.AddWithValue("phone", phone);
            command.Parameters.AddWithValue("pin", pinHash);
            if (await command.ExecuteNonQueryAsync(cancellationToken) == 0)
                throw new ArgumentException("Ya existe una cuenta con este teléfono. Inicia sesión.");
        }
        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = "INSERT INTO cuentas_fidelizacion(id_cliente) VALUES(@customer) ON CONFLICT(id_cliente) DO NOTHING;";
            command.Parameters.AddWithValue("customer", customerId);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
        return await CreateSessionAsync(customerId, request.DeviceName, cancellationToken);
    }

    public async Task<CustomerSession?> LoginAsync(LoginCustomer request, CancellationToken cancellationToken)
    {
        var phone = NormalizePhone(request.Phone);
        await using var command = dataSource.CreateCommand("""
            SELECT a.id_cliente,a.pin_hash FROM cuentas_cliente_app a
             WHERE a.telefono_normalizado=@phone AND a.estado='activa' AND a.pin_hash IS NOT NULL;
            """);
        command.Parameters.AddWithValue("phone", phone);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;
        var customerId = reader.GetInt64(0);
        var hash = reader.GetString(1);
        if (!passwordHasher.Verify(request.Pin, hash)) return null;
        return await CreateSessionAsync(customerId, request.DeviceName, cancellationToken);
    }

    public async Task<CustomerSession> LoginWithGoogleAsync(
        VerifiedGoogleIdentity identity, string phoneValue, string? deviceName, CancellationToken cancellationToken)
    {
        var phone = NormalizePhone(phoneValue);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        await using (var advisory = connection.CreateCommand())
        {
            advisory.Transaction = transaction;
            advisory.CommandText = "SELECT pg_advisory_xact_lock(hashtextextended(@subject,0));";
            advisory.Parameters.AddWithValue("subject", identity.Subject);
            await advisory.ExecuteNonQueryAsync(cancellationToken);
        }
        await using (var advisory = connection.CreateCommand())
        {
            advisory.Transaction = transaction;
            advisory.CommandText = "SELECT pg_advisory_xact_lock(hashtextextended(@phone,0));";
            advisory.Parameters.AddWithValue("phone", phone);
            await advisory.ExecuteNonQueryAsync(cancellationToken);
        }
        long customerId;
        await using (var lookup = connection.CreateCommand())
        {
            lookup.Transaction = transaction;
            lookup.CommandText = "SELECT id_cuenta_app,id_cliente FROM cuentas_cliente_app WHERE google_subject=@subject;";
            lookup.Parameters.AddWithValue("subject", identity.Subject);
            await using var reader = await lookup.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                var accountId = reader.GetInt64(0);
                customerId = reader.GetInt64(1);
                await reader.DisposeAsync();
                await EnsurePhoneAvailableAsync(connection, transaction, phone, accountId, cancellationToken);
                await UpdateGoogleAccountPhoneAsync(connection, transaction, accountId, customerId, phone, identity, cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return await CreateSessionAsync(customerId, deviceName, cancellationToken);
            }
        }

        long? phoneAccountId = null;
        await using (var phoneLookup = connection.CreateCommand())
        {
            phoneLookup.Transaction = transaction;
            phoneLookup.CommandText = "SELECT id_cuenta_app,id_cliente,google_subject FROM cuentas_cliente_app WHERE telefono_normalizado=@phone;";
            phoneLookup.Parameters.AddWithValue("phone", phone);
            await using var reader = await phoneLookup.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                phoneAccountId = reader.GetInt64(0);
                customerId = reader.GetInt64(1);
                if (!reader.IsDBNull(2) && reader.GetString(2) != identity.Subject)
                    throw new ArgumentException("Este teléfono ya está asociado a otra cuenta de Google.");
            }
            else customerId = 0;
        }

        if (phoneAccountId is not null)
        {
            await UpdateGoogleAccountPhoneAsync(connection, transaction, phoneAccountId.Value, customerId, phone, identity, cancellationToken);
        }
        else
        {
            await using (var customer = connection.CreateCommand())
            {
                customer.Transaction = transaction;
                customer.CommandText = """
                    WITH existing AS (
                        SELECT c.id_cliente
                          FROM clientes c
                         WHERE NOT EXISTS(SELECT 1 FROM cuentas_cliente_app a WHERE a.id_cliente=c.id_cliente)
                           AND (RIGHT(REGEXP_REPLACE(c.telefono,'\D','','g'),9)=RIGHT(@phone,9)
                                OR LOWER(c.email)=@email)
                         ORDER BY (RIGHT(REGEXP_REPLACE(c.telefono,'\D','','g'),9)=RIGHT(@phone,9)) DESC,c.creado_en
                         LIMIT 1
                    ), inserted AS (
                        INSERT INTO clientes(nombre,telefono,email)
                        SELECT @name,@phone,@email WHERE NOT EXISTS(SELECT 1 FROM existing)
                        RETURNING id_cliente
                    )
                    SELECT id_cliente FROM existing UNION ALL SELECT id_cliente FROM inserted LIMIT 1;
                    """;
                customer.Parameters.AddWithValue("name", Required(identity.Name,"nombre",150));
                customer.Parameters.AddWithValue("phone", phone);
                customer.Parameters.AddWithValue("email", identity.Email);
                customerId = Convert.ToInt64(await customer.ExecuteScalarAsync(cancellationToken));
            }
            await using (var customerUpdate = connection.CreateCommand())
            {
                customerUpdate.Transaction = transaction;
                customerUpdate.CommandText = "UPDATE clientes SET telefono=@phone,email=COALESCE(email,@email) WHERE id_cliente=@customer;";
                customerUpdate.Parameters.AddWithValue("phone", phone);
                customerUpdate.Parameters.AddWithValue("email", identity.Email);
                customerUpdate.Parameters.AddWithValue("customer", customerId);
                await customerUpdate.ExecuteNonQueryAsync(cancellationToken);
            }
            await using var account = connection.CreateCommand();
            account.Transaction = transaction;
            account.CommandText = """
                INSERT INTO cuentas_cliente_app(id_cliente,telefono_normalizado,pin_hash,google_subject)
                VALUES(@customer,@phone,NULL,@subject);
                INSERT INTO cuentas_fidelizacion(id_cliente) VALUES(@customer) ON CONFLICT(id_cliente) DO NOTHING;
                """;
            account.Parameters.AddWithValue("customer",customerId);
            account.Parameters.AddWithValue("phone",phone);
            account.Parameters.AddWithValue("subject",identity.Subject);
            await account.ExecuteNonQueryAsync(cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
        return await CreateSessionAsync(customerId, deviceName, cancellationToken);
    }

    private static async Task EnsurePhoneAvailableAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, string phone, long accountId, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT EXISTS(SELECT 1 FROM cuentas_cliente_app WHERE telefono_normalizado=@phone AND id_cuenta_app<>@account);";
        command.Parameters.AddWithValue("phone", phone);
        command.Parameters.AddWithValue("account", accountId);
        if (Convert.ToBoolean(await command.ExecuteScalarAsync(cancellationToken)))
            throw new ArgumentException("Este teléfono ya está asociado a otra cuenta.");
    }

    private static async Task UpdateGoogleAccountPhoneAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, long accountId, long customerId, string phone,
        VerifiedGoogleIdentity identity, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE cuentas_cliente_app
               SET telefono_normalizado=@phone,google_subject=@subject,actualizado_en=NOW()
             WHERE id_cuenta_app=@account;
            UPDATE clientes
               SET telefono=@phone,email=COALESCE(email,@email)
             WHERE id_cliente=@customer;
            INSERT INTO cuentas_fidelizacion(id_cliente) VALUES(@customer) ON CONFLICT(id_cliente) DO NOTHING;
            """;
        command.Parameters.AddWithValue("phone", phone);
        command.Parameters.AddWithValue("subject", identity.Subject);
        command.Parameters.AddWithValue("account", accountId);
        command.Parameters.AddWithValue("customer", customerId);
        command.Parameters.AddWithValue("email", identity.Email);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<CustomerProfile?> GetSessionCustomerAsync(string token, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(token)) return null;
        await using var command = dataSource.CreateCommand("""
            SELECT c.id_cliente,c.nombre,c.telefono,c.email,COALESCE(f.saldo_puntos,0)
              FROM sesiones_cliente_app s
              JOIN cuentas_cliente_app a ON a.id_cuenta_app=s.id_cuenta_app
              JOIN clientes c ON c.id_cliente=a.id_cliente
              LEFT JOIN cuentas_fidelizacion f ON f.id_cliente=c.id_cliente AND f.estado='activa'
             WHERE s.token_hash=@hash AND s.revocada_en IS NULL AND s.expira_en>NOW() AND a.estado='activa';
            """);
        command.Parameters.AddWithValue("hash", HashToken(token));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadProfile(reader) : null;
    }

    public async Task RevokeSessionAsync(string token, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(token)) return;
        await using var command = dataSource.CreateCommand(
            "UPDATE sesiones_cliente_app SET revocada_en=NOW() WHERE token_hash=@hash AND revocada_en IS NULL;");
        command.Parameters.AddWithValue("hash", HashToken(token));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<CustomerHome> GetHomeAsync(long? customerId, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        CustomerProfile? profile = null;
        if (customerId is not null)
        {
            await using var profileCommand = connection.CreateCommand();
            profileCommand.CommandText = """
                SELECT c.id_cliente,c.nombre,c.telefono,c.email,COALESCE(f.saldo_puntos,0)
                  FROM clientes c LEFT JOIN cuentas_fidelizacion f ON f.id_cliente=c.id_cliente AND f.estado='activa'
                 WHERE c.id_cliente=@id;
                """;
            profileCommand.Parameters.AddWithValue("id", customerId.Value);
            await using var reader = await profileCommand.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken)) profile = ReadProfile(reader);
        }
        var promotions = new List<CustomerPromotion>();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT a.id_promocion,p.id_producto,p.nombre_producto,p.descripcion,p.precio,
                       a.productos_incluidos,a.salsas_incluidas,a.habilitada_retiro,a.habilitada_delivery
                  FROM promociones_admin a JOIN productos p ON p.id_producto=a.id_producto
                 WHERE a.activa AND p.activo AND p.disponible
                   AND (a.vigente_desde IS NULL OR a.vigente_desde<=NOW())
                   AND (a.vigente_hasta IS NULL OR a.vigente_hasta>NOW())
                 ORDER BY a.actualizado_en DESC,p.nombre_producto;
                """;
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken)) promotions.Add(new(
                reader.GetInt64(0),reader.GetInt64(1),reader.GetString(2),reader.IsDBNull(3)?null:reader.GetString(3),
                reader.GetDecimal(4),reader.IsDBNull(5)?null:reader.GetString(5),reader.GetInt32(6),reader.GetBoolean(7),reader.GetBoolean(8)));
        }
        var rewards = new List<CustomerReward>();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT pc.id_producto_canje,p.id_producto,p.nombre_producto,p.descripcion,pc.coste_puntos,
                       pc.stock_disponible,pc.limite_por_pedido
                  FROM productos_canje pc JOIN productos p ON p.id_producto=pc.id_producto
                 WHERE pc.activo AND p.activo AND p.disponible AND NOT p.requiere_configuracion
                   AND pc.vigente_desde<=NOW() AND (pc.vigente_hasta IS NULL OR pc.vigente_hasta>NOW())
                   AND (pc.stock_disponible IS NULL OR pc.stock_disponible>0)
                 ORDER BY pc.coste_puntos,p.nombre_producto;
                """;
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var cost=reader.GetInt32(4);
                rewards.Add(new(reader.GetInt64(0),reader.GetInt64(1),reader.GetString(2),reader.IsDBNull(3)?null:reader.GetString(3),cost,
                    reader.IsDBNull(5)?null:reader.GetInt32(5),reader.IsDBNull(6)?null:reader.GetInt32(6),profile is not null && profile.Points>=cost));
            }
        }
        await using var shift = connection.CreateCommand();
        shift.CommandText = "SELECT EXISTS(SELECT 1 FROM turnos WHERE estado='abierto');";
        var isOpen = Convert.ToBoolean(await shift.ExecuteScalarAsync(cancellationToken));
        return new CustomerHome(isOpen, profile, promotions, rewards);
    }

    public async Task RegisterNotificationDeviceAsync(
        long customerId, RegisterNotificationDevice request, CancellationToken cancellationToken)
    {
        var token = Required(request.DeviceToken, "token del dispositivo", 4096);
        var platform = Required(request.Platform, "plataforma", 20).ToLowerInvariant();
        if (platform != "android") throw new ArgumentException("La plataforma de notificaciones no es compatible.");
        var deviceName = string.IsNullOrWhiteSpace(request.DeviceName)
            ? null
            : request.DeviceName.Trim()[..Math.Min(150, request.DeviceName.Trim().Length)];
        await using var command = dataSource.CreateCommand("""
            INSERT INTO dispositivos_notificacion_cliente
                (id_cliente,token_fcm,plataforma,nombre_dispositivo,activo,registrado_en,ultimo_uso_en)
            VALUES (@customer,@token,@platform,@device,TRUE,NOW(),NOW())
            ON CONFLICT(token_fcm) DO UPDATE SET
                id_cliente=EXCLUDED.id_cliente,
                plataforma=EXCLUDED.plataforma,
                nombre_dispositivo=EXCLUDED.nombre_dispositivo,
                activo=TRUE,
                ultimo_uso_en=NOW();
            """);
        command.Parameters.AddWithValue("customer", customerId);
        command.Parameters.AddWithValue("token", token);
        command.Parameters.AddWithValue("platform", platform);
        command.Parameters.Add("device", NpgsqlDbType.Varchar).Value = (object?)deviceName ?? DBNull.Value;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task DeactivateNotificationDeviceAsync(
        long customerId, string deviceToken, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(deviceToken)) return;
        await using var command = dataSource.CreateCommand("""
            UPDATE dispositivos_notificacion_cliente
               SET activo=FALSE,ultimo_uso_en=NOW()
             WHERE id_cliente=@customer AND token_fcm=@token;
            """);
        command.Parameters.AddWithValue("customer", customerId);
        command.Parameters.AddWithValue("token", deviceToken.Trim());
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task<CustomerSession> CreateSessionAsync(long customerId, string? device, CancellationToken cancellationToken)
    {
        var token=Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+','-').Replace('/','_');
        var expires=DateTimeOffset.UtcNow.AddDays(30);
        await using var command=dataSource.CreateCommand("""
            DELETE FROM sesiones_cliente_app WHERE expira_en<=NOW();
            INSERT INTO sesiones_cliente_app(id_cuenta_app,token_hash,dispositivo,expira_en)
            SELECT id_cuenta_app,@hash,@device,@expires FROM cuentas_cliente_app WHERE id_cliente=@customer;
            """);
        command.Parameters.AddWithValue("hash",HashToken(token));
        command.Parameters.Add("device",NpgsqlDbType.Varchar).Value=string.IsNullOrWhiteSpace(device)?DBNull.Value:device.Trim()[..Math.Min(150,device.Trim().Length)];
        command.Parameters.AddWithValue("expires",expires); command.Parameters.AddWithValue("customer",customerId);
        await command.ExecuteNonQueryAsync(cancellationToken);
        var profile=await GetProfileAsync(customerId,cancellationToken) ?? throw new InvalidOperationException("No se pudo cargar el cliente.");
        return new CustomerSession(token,expires,profile);
    }

    private async Task<CustomerProfile?> GetProfileAsync(long id,CancellationToken cancellationToken)
    {
        await using var command=dataSource.CreateCommand("SELECT c.id_cliente,c.nombre,c.telefono,c.email,COALESCE(f.saldo_puntos,0) FROM clientes c LEFT JOIN cuentas_fidelizacion f ON f.id_cliente=c.id_cliente AND f.estado='activa' WHERE c.id_cliente=@id;");
        command.Parameters.AddWithValue("id",id); await using var reader=await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)?ReadProfile(reader):null;
    }
    private static CustomerProfile ReadProfile(NpgsqlDataReader r)
    {
        var phone=r.GetString(2);
        if(phone.Length==19 && phone[0]=='g') phone=string.Empty;
        return new(r.GetInt64(0),r.GetString(1),phone,r.IsDBNull(3)?null:r.GetString(3),r.GetInt32(4));
    }
    private static string NormalizePhone(string value){var digits=new string((value??"").Where(char.IsDigit).ToArray());if(digits.Length<8||digits.Length>15)throw new ArgumentException("Ingresa un teléfono válido.");return digits.Length==9?"56"+digits:digits;}
    private static string Required(string? value,string field,int max){if(string.IsNullOrWhiteSpace(value))throw new ArgumentException($"El campo {field} es obligatorio.");var v=value.Trim();if(v.Length>max)throw new ArgumentException($"El campo {field} no puede superar {max} caracteres.");return v;}
    private static string HashToken(string token)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();
}
