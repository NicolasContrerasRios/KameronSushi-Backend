namespace KameronSushi.Application.Abstractions;

public interface IPushNotificationService
{
    Task NotifyLocalOpenedAsync(CancellationToken cancellationToken);
    Task NotifyOrderReadyAsync(long orderId, CancellationToken cancellationToken);
    Task NotifyOrderOutForDeliveryAsync(long orderId, CancellationToken cancellationToken);
}
