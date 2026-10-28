using System.Text;
using FirebaseAdmin;
using FirebaseAdmin.Messaging;
using Google.Apis.Auth.OAuth2;
using KameronSushi.Application.Abstractions;
using KameronSushi.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace KameronSushi.Infrastructure.Notifications;

public sealed class FirebasePushNotificationService(
    NpgsqlDataSource dataSource,
    IOptions<FirebaseOptions> options,
    ILogger<FirebasePushNotificationService> logger) : IPushNotificationService
{
    private const string FirebaseAppName = "kameron-sushi-notifications";
    private readonly FirebaseOptions settings = options.Value;
    private FirebaseMessaging? messaging;
    private bool initializationAttempted;
    private readonly object initializationLock = new();

    public async Task NotifyLocalOpenedAsync(CancellationToken cancellationToken)
    {
        await ExecuteSafelyAsync("local_abierto", async () =>
        {
            var tokens = await GetTokensAsync(null, cancellationToken);
            await SendAsync(tokens, "Kameron Sushi está atendiendo",
                "El local ya está recibiendo pedidos para retiro y delivery en La Serena.",
                "local_abierto", null, cancellationToken);
        });
    }

    public async Task NotifyOrderReadyAsync(long orderId, CancellationToken cancellationToken)
    {
        await ExecuteSafelyAsync("pedido_listo", async () =>
        {
            var target = await GetOrderTargetAsync(orderId, cancellationToken);
            if (target is null || !target.DeliveryType.Equals("retiro", StringComparison.OrdinalIgnoreCase)) return;
            await SendAsync(target.Tokens, $"Pedido #{orderId} listo",
                "Tu pedido está listo para retirar en Kameron Sushi.",
                "pedido_listo", orderId, cancellationToken);
        });
    }

    public async Task NotifyOrderOutForDeliveryAsync(long orderId, CancellationToken cancellationToken)
    {
        await ExecuteSafelyAsync("pedido_en_reparto", async () =>
        {
            var target = await GetOrderTargetAsync(orderId, cancellationToken);
            if (target is null || !target.DeliveryType.Equals("delivery", StringComparison.OrdinalIgnoreCase)) return;
            await SendAsync(target.Tokens, $"Pedido #{orderId} en reparto",
                "Tu pedido salió del local y va en camino a tu dirección.",
                "pedido_en_reparto", orderId, cancellationToken);
        });
    }

    private async Task ExecuteSafelyAsync(string eventType, Func<Task> action)
    {
        try { await action(); }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception)
        {
            // El pedido o turno ya fue confirmado; una falla de notificación no revierte la operación.
            logger.LogError(exception, "No se pudo procesar la notificación {EventType}", eventType);
        }
    }

    private async Task SendAsync(
        IReadOnlyList<string> tokens, string title, string body, string eventType,
        long? orderId, CancellationToken cancellationToken)
    {
        var client = GetMessaging();
        if (client is null || tokens.Count == 0) return;
        foreach (var batch in tokens.Distinct(StringComparer.Ordinal).Chunk(500))
        {
            try
            {
                var data = new Dictionary<string, string> { ["type"] = eventType };
                if (orderId is long id) data["orderId"] = id.ToString();
                var response = await client.SendEachForMulticastAsync(new MulticastMessage
                {
                    Fids = batch,
                    Notification = new Notification { Title = title, Body = body },
                    Data = data,
                    Android = new AndroidConfig
                    {
                        Priority = Priority.High,
                        Notification = new AndroidNotification
                        {
                            ChannelId = eventType == "local_abierto" ? "local_updates" : "orders",
                            Sound = "default"
                        }
                    }
                }, cancellationToken);
                var invalidTokens = response.Responses.Select((result, index) => (result, index))
                    .Where(value => !value.result.IsSuccess &&
                        value.result.Exception?.MessagingErrorCode is MessagingErrorCode.Unregistered or MessagingErrorCode.SenderIdMismatch)
                    .Select(value => batch[value.index]).ToArray();
                if (invalidTokens.Length > 0) await DeactivateTokensAsync(invalidTokens, cancellationToken);
                logger.LogInformation("Notificación {EventType}: {Success} entregas aceptadas y {Failures} fallidas",
                    eventType, response.SuccessCount, response.FailureCount);
            }
            catch (Exception exception)
            {
                // Una falla externa de FCM nunca debe impedir abrir el local ni cambiar el estado del pedido.
                logger.LogError(exception, "No se pudo enviar la notificación {EventType}", eventType);
            }
        }
    }

    private FirebaseMessaging? GetMessaging()
    {
        if (!settings.Enabled) return null;
        if (messaging is not null || initializationAttempted) return messaging;
        lock (initializationLock)
        {
            if (messaging is not null || initializationAttempted) return messaging;
            initializationAttempted = true;
            try
            {
                if (string.IsNullOrWhiteSpace(settings.ProjectId))
                {
                    logger.LogWarning("Firebase está habilitado, pero falta ProjectId");
                    return null;
                }
                var json = ReadServiceAccountJson();
                if (json is null)
                {
                    logger.LogWarning(
                        "Firebase está habilitado, pero no existe el Secret File {Path} ni se configuró ServiceAccountJsonBase64",
                        settings.ServiceAccountPath);
                    return null;
                }
                FirebaseApp? app = null;
                try { app = FirebaseApp.GetInstance(FirebaseAppName); }
                catch (ArgumentException) { /* Algunas versiones lanzan excepción si aún no existe. */ }
                app ??= FirebaseApp.Create(new AppOptions
                {
                    Credential = CredentialFactory.FromJson<ServiceAccountCredential>(json).ToGoogleCredential(),
                    ProjectId = settings.ProjectId
                }, FirebaseAppName);
                messaging = FirebaseMessaging.GetMessaging(app);
                return messaging;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Firebase no pudo inicializarse; las notificaciones quedan deshabilitadas");
                return null;
            }
        }
    }

    private string? ReadServiceAccountJson()
    {
        if (!string.IsNullOrWhiteSpace(settings.ServiceAccountPath) && File.Exists(settings.ServiceAccountPath))
            return File.ReadAllText(settings.ServiceAccountPath);
        if (!string.IsNullOrWhiteSpace(settings.ServiceAccountJsonBase64))
            return Encoding.UTF8.GetString(Convert.FromBase64String(settings.ServiceAccountJsonBase64));
        return null;
    }

    private async Task<IReadOnlyList<string>> GetTokensAsync(long? customerId, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand("""
            SELECT token_fcm
              FROM dispositivos_notificacion_cliente
             WHERE activo=TRUE
               AND ultimo_uso_en >= NOW() - INTERVAL '90 days'
               AND (@customer IS NULL OR id_cliente=@customer);
            """);
        command.Parameters.Add("customer", NpgsqlTypes.NpgsqlDbType.Bigint).Value = (object?)customerId ?? DBNull.Value;
        var tokens = new List<string>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) tokens.Add(reader.GetString(0));
        return tokens;
    }

    private async Task<OrderTarget?> GetOrderTargetAsync(long orderId, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand("SELECT id_cliente,tipo_entrega FROM pedidos WHERE id_pedido=@id;");
        command.Parameters.AddWithValue("id", orderId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken) || reader.IsDBNull(0)) return null;
        var customerId = reader.GetInt64(0);
        var deliveryType = reader.GetString(1);
        await reader.DisposeAsync();
        return new OrderTarget(deliveryType, await GetTokensAsync(customerId, cancellationToken));
    }

    private async Task DeactivateTokensAsync(IReadOnlyCollection<string> tokens, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand("""
            UPDATE dispositivos_notificacion_cliente SET activo=FALSE,ultimo_uso_en=NOW()
             WHERE token_fcm=ANY(@tokens);
            """);
        command.Parameters.AddWithValue("tokens", tokens.ToArray());
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private sealed record OrderTarget(string DeliveryType, IReadOnlyList<string> Tokens);
}
