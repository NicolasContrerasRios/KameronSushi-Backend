namespace KameronSushi.Application.Abstractions;

public interface IProductImageStorage
{
    Task<string> UploadAsync(long productId, Stream content, string fileName, CancellationToken cancellationToken);
    Task DeleteAsync(long productId, CancellationToken cancellationToken);
}
