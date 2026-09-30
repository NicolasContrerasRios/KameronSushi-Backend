using KameronSushi.Application.Auth;

namespace KameronSushi.Application.Abstractions;

public interface IAuthStore
{
    Task<UserSession?> LoginAsync(string email, string password, string? deviceName, CancellationToken cancellationToken);
    Task<AuthenticatedUser?> GetSessionUserAsync(string token, CancellationToken cancellationToken);
    Task RevokeSessionAsync(string token, CancellationToken cancellationToken);
    Task<bool> HasAdministratorAsync(CancellationToken cancellationToken);
    Task<AdminUser> BootstrapAdministratorAsync(CreateAdminUser user, CancellationToken cancellationToken);
    Task<IReadOnlyList<AdminUser>> GetUsersAsync(CancellationToken cancellationToken);
    Task<AdminUser> CreateUserAsync(CreateAdminUser user, CancellationToken cancellationToken);
    Task<AdminUser?> UpdateUserAsync(long userId, UpdateAdminUser user, long actingUserId, CancellationToken cancellationToken);
}

