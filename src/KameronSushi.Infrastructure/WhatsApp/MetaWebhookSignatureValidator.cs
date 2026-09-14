using System.Security.Cryptography;
using System.Text;
using KameronSushi.Application.Abstractions;
using KameronSushi.Infrastructure.Configuration;
using Microsoft.Extensions.Options;

namespace KameronSushi.Infrastructure.WhatsApp;

public sealed class MetaWebhookSignatureValidator(IOptions<WhatsAppOptions> options) : IWebhookSignatureValidator
{
    public bool IsValid(ReadOnlySpan<byte> payload, string? signatureHeader)
    {
        var secret = options.Value.AppSecret;
        if (string.IsNullOrWhiteSpace(secret) || string.IsNullOrWhiteSpace(signatureHeader) ||
            !signatureHeader.StartsWith("sha256=", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        byte[] received;
        try
        {
            received = Convert.FromHexString(signatureHeader[7..]);
        }
        catch (FormatException)
        {
            return false;
        }

        if (received.Length != 32)
        {
            return false;
        }

        var expected = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), payload);
        return CryptographicOperations.FixedTimeEquals(expected, received);
    }
}
