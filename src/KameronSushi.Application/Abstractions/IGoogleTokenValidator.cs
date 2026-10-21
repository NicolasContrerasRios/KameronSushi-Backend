using KameronSushi.Application.Customers;

namespace KameronSushi.Application.Abstractions;

public interface IGoogleTokenValidator
{
    Task<VerifiedGoogleIdentity?> ValidateAsync(string idToken, CancellationToken cancellationToken);
}
