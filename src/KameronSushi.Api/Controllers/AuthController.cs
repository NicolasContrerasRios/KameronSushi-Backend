using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using KameronSushi.Application.Abstractions;
using KameronSushi.Application.Auth;
using KameronSushi.Infrastructure.Configuration;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace KameronSushi.Api.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController(IAuthStore authStore, IOptions<AuthOptions> options) : ControllerBase
{
    [AllowAnonymous]
    [EnableRateLimiting("authentication")]
    [HttpPost("login")]
    public async Task<ActionResult<UserSession>> Login(LoginRequest request, CancellationToken cancellationToken)
    {
        var session = await authStore.LoginAsync(request.Email, request.Password, request.DeviceName, cancellationToken);
        return session is null
            ? Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Acceso rechazado", detail: "Correo o PIN incorrectos, o la cuenta no está activa.")
            : Ok(session);
    }

    [Authorize]
    [HttpGet("me")]
    public ActionResult<AuthenticatedUser> Me() => Ok(new AuthenticatedUser(
        GetUserId(), User.Identity?.Name ?? string.Empty,
        User.FindFirstValue(ClaimTypes.Email) ?? string.Empty,
        User.FindAll(ClaimTypes.Role).Select(claim => claim.Value).ToArray()));

    [Authorize]
    [HttpPost("logout")]
    public async Task<IActionResult> Logout(CancellationToken cancellationToken)
    {
        await authStore.RevokeSessionAsync(ReadBearerToken(), cancellationToken);
        return NoContent();
    }

    [AllowAnonymous]
    [EnableRateLimiting("authentication")]
    [HttpPost("bootstrap")]
    public async Task<ActionResult<AdminUser>> Bootstrap(
        BootstrapAdministrator request, CancellationToken cancellationToken)
    {
        var expectedKey = options.Value.BootstrapKey;
        if (string.IsNullOrWhiteSpace(expectedKey))
            return Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "Configuración inicial no habilitada", detail: "Configura Auth__BootstrapKey en el servidor.");
        if (!FixedEquals(request.SetupKey, expectedKey)) return Unauthorized();
        if (await authStore.HasAdministratorAsync(cancellationToken))
            return Conflict(new ProblemDetails { Title = "Configuración completada", Detail = "Ya existe un administrador." });
        try
        {
            return Ok(await authStore.BootstrapAdministratorAsync(
                new CreateAdminUser(request.Name, request.Email, request.Password, ["administrador"]), cancellationToken));
        }
        catch (ArgumentException exception)
        {
            return InvalidUser(exception);
        }
        catch (InvalidOperationException exception)
        {
            return Conflict(new ProblemDetails { Title = "Configuración completada", Detail = exception.Message });
        }
    }

    private long GetUserId() => long.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    private string ReadBearerToken() => Request.Headers.Authorization.ToString()[7..].Trim();
    private ObjectResult InvalidUser(ArgumentException exception) => Problem(
        statusCode: StatusCodes.Status400BadRequest, title: "Usuario inválido", detail: exception.Message);

    private static bool FixedEquals(string supplied, string expected)
    {
        var suppliedBytes = Encoding.UTF8.GetBytes(supplied ?? string.Empty);
        var expectedBytes = Encoding.UTF8.GetBytes(expected);
        return suppliedBytes.Length == expectedBytes.Length && CryptographicOperations.FixedTimeEquals(suppliedBytes, expectedBytes);
    }
}
