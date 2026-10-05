using KameronSushi.Infrastructure.Configuration;
using Microsoft.Extensions.Options;
using Twilio.Security;

namespace KameronSushi.Infrastructure.WhatsApp;

public sealed class TwilioWebhookSignatureValidator(IOptions<TwilioOptions> options)
{
    public bool IsValid(string url, IReadOnlyDictionary<string, string> parameters, string? signature)
    {
        var authToken = options.Value.AuthToken;
        if (string.IsNullOrWhiteSpace(authToken) || string.IsNullOrWhiteSpace(signature))
        {
            return false;
        }

        var validator = new RequestValidator(authToken);
        return validator.Validate(url, new Dictionary<string, string>(parameters), signature);
    }
}
