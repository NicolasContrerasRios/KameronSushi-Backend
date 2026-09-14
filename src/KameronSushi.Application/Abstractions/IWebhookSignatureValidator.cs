namespace KameronSushi.Application.Abstractions;

public interface IWebhookSignatureValidator
{
    bool IsValid(ReadOnlySpan<byte> payload, string? signatureHeader);
}
