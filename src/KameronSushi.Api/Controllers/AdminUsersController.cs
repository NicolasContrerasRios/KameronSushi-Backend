using System.Security.Claims;
using KameronSushi.Application.Abstractions;
using KameronSushi.Application.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace KameronSushi.Api.Controllers;

[ApiController]
[Authorize(Roles = "administrador")]
[Route("api/admin/users")]
public sealed class AdminUsersController(IAuthStore authStore) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<AdminUser>>> Get(CancellationToken cancellationToken) =>
        Ok(await authStore.GetUsersAsync(cancellationToken));

    [HttpPost]
    public async Task<ActionResult<AdminUser>> Create(CreateAdminUser request, CancellationToken cancellationToken)
    {
        try
        {
            var created = await authStore.CreateUserAsync(request, cancellationToken);
            return CreatedAtAction(nameof(Get), created);
        }
        catch (ArgumentException exception)
        {
            return InvalidUser(exception);
        }
    }

    [HttpPut("{userId:long}")]
    public async Task<ActionResult<AdminUser>> Update(
        long userId, UpdateAdminUser request, CancellationToken cancellationToken)
    {
        try
        {
            var updated = await authStore.UpdateUserAsync(userId, request, GetUserId(), cancellationToken);
            return updated is null ? NotFound() : Ok(updated);
        }
        catch (ArgumentException exception)
        {
            return InvalidUser(exception);
        }
    }

    private long GetUserId() => long.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    private ObjectResult InvalidUser(ArgumentException exception) => Problem(
        statusCode: StatusCodes.Status400BadRequest, title: "Usuario inválido", detail: exception.Message);
}
