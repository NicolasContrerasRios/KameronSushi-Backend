using Google.Apis.Auth;
using KameronSushi.Application.Abstractions;
using KameronSushi.Application.Customers;
using KameronSushi.Infrastructure.Configuration;
using Microsoft.Extensions.Options;

namespace KameronSushi.Infrastructure.Auth;

public sealed class GoogleTokenValidator(IOptions<GoogleAuthOptions> options) : IGoogleTokenValidator
{
    public async Task<VerifiedGoogleIdentity?> ValidateAsync(string idToken, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(idToken) || string.IsNullOrWhiteSpace(options.Value.ClientId)) return null;
        try
        {
            var payload = await GoogleJsonWebSignature.ValidateAsync(idToken, new GoogleJsonWebSignature.ValidationSettings
            {
                Audience = [options.Value.ClientId]
            });
            if (payload.EmailVerified != true || string.IsNullOrWhiteSpace(payload.Subject) || string.IsNullOrWhiteSpace(payload.Email))
                return null;
            var name = string.IsNullOrWhiteSpace(payload.Name) ? payload.Email.Split('@')[0] : payload.Name;
            return new VerifiedGoogleIdentity(payload.Subject, payload.Email.ToLowerInvariant(), name);
        }
        catch (InvalidJwtException)
        {
            return null;
        }
    }
}
