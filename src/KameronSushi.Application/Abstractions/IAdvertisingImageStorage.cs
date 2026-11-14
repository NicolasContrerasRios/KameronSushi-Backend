namespace KameronSushi.Application.Abstractions;

public interface IAdvertisingImageStorage
{
    Task<string> UploadAsync(long panelId, Stream content, string fileName, CancellationToken cancellationToken);
    Task DeleteAsync(long panelId, CancellationToken cancellationToken);
}
