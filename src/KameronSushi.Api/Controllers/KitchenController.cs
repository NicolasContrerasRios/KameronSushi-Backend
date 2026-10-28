using KameronSushi.Application.Abstractions;
using KameronSushi.Application.Pos;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;

namespace KameronSushi.Api.Controllers;

[ApiController]
[Authorize(Roles = "administrador,cocina")]
[Route("api/kitchen/orders")]
public sealed class KitchenController(IPosStore store, IPushNotificationService notifications) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<KitchenOrderSummary>>> GetOrders(CancellationToken cancellationToken) =>
        Ok(await store.GetKitchenOrdersAsync(cancellationToken));

    [HttpGet("performance")]
    public async Task<ActionResult<IReadOnlyList<KitchenPerformanceSummary>>> GetPerformance(CancellationToken cancellationToken) =>
        Ok(await store.GetKitchenPerformanceAsync(cancellationToken));

    [HttpPatch("{orderId:long}/ready")]
    public async Task<IActionResult> MarkReady(long orderId, CancellationToken cancellationToken)
    {
        try
        {
            if (!await store.MarkOrderReadyAsync(orderId, GetUserId(), cancellationToken)) return NotFound();
            await notifications.NotifyOrderReadyAsync(orderId, cancellationToken);
            return NoContent();
        }
        catch (InvalidOperationException exception)
        {
            return Conflict(new ProblemDetails { Title = "Transición no permitida", Detail = exception.Message });
        }
    }

    private long GetUserId() => long.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
}
