namespace KameronSushi.Application.Auth;

public sealed record AuthenticatedUser(
    long UserId,
    string Name,
    string Email,
    IReadOnlyList<string> Roles);

public sealed record UserSession(
    string Token,
    DateTimeOffset ExpiresAt,
    AuthenticatedUser User);

public sealed record AdminUser(
    long UserId,
    string Name,
    string Email,
    string Status,
    IReadOnlyList<string> Roles,
    DateTimeOffset CreatedAt);

public sealed record CreateAdminUser(
    string Name,
    string Email,
    string Password,
    IReadOnlyList<string> Roles);

public sealed record UpdateAdminUser(
    string Name,
    string Email,
    string Status,
    IReadOnlyList<string> Roles,
    string? NewPassword);

public sealed record BootstrapAdministrator(
    string SetupKey,
    string Name,
    string Email,
    string Password);

public sealed record LoginRequest(string Email, string Password, string? DeviceName);

