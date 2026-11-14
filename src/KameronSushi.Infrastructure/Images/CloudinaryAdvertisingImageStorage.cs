using CloudinaryDotNet;
using CloudinaryDotNet.Actions;
using KameronSushi.Application.Abstractions;
using KameronSushi.Infrastructure.Configuration;
using Microsoft.Extensions.Options;

namespace KameronSushi.Infrastructure.Images;

public sealed class CloudinaryAdvertisingImageStorage : IAdvertisingImageStorage
{
    private readonly Cloudinary? cloudinary;
    private readonly bool configured;

    public CloudinaryAdvertisingImageStorage(IOptions<CloudinaryOptions> options)
    {
        var value = options.Value;
        configured = !string.IsNullOrWhiteSpace(value.CloudName)
            && !string.IsNullOrWhiteSpace(value.ApiKey)
            && !string.IsNullOrWhiteSpace(value.ApiSecret);
        if (configured)
        {
            cloudinary = new Cloudinary(new Account(value.CloudName, value.ApiKey, value.ApiSecret));
            cloudinary.Api.Secure = true;
        }
    }

    public async Task<string> UploadAsync(
        long panelId, Stream content, string fileName, CancellationToken cancellationToken)
    {
        EnsureConfigured();
        var parameters = new ImageUploadParams
        {
            File = new FileDescription(fileName, content),
            PublicId = PublicId(panelId),
            Overwrite = true,
            Invalidate = true,
            UniqueFilename = false,
            UseFilename = false,
            Format = "jpg",
            Transformation = new Transformation()
                .Width(1920).Height(1080).Crop("fill").Gravity("auto").Quality("auto:good")
        };
        var result = await cloudinary!.UploadAsync(parameters, cancellationToken);
        if (result.Error is not null)
            throw new InvalidOperationException($"Cloudinary rechazó la imagen: {result.Error.Message}");
        return result.SecureUrl?.AbsoluteUri
            ?? throw new InvalidOperationException("Cloudinary no devolvió una URL segura para la imagen.");
    }

    public async Task DeleteAsync(long panelId, CancellationToken cancellationToken)
    {
        EnsureConfigured();
        var result = await cloudinary!.DestroyAsync(new DeletionParams(PublicId(panelId))
        {
            ResourceType = ResourceType.Image,
            Invalidate = true
        });
        if (result.Error is not null)
            throw new InvalidOperationException($"Cloudinary no pudo eliminar la imagen: {result.Error.Message}");
    }

    private void EnsureConfigured()
    {
        if (!configured)
            throw new InvalidOperationException(
                "Cloudinary no está configurado. Agrega Cloudinary__CloudName, Cloudinary__ApiKey y Cloudinary__ApiSecret en Render.");
    }

    private static string PublicId(long panelId) => $"kameron-sushi/advertising/{panelId}";
}
