using KameronSushi.Application.Abstractions;
using KameronSushi.Application.Pos;
using Microsoft.AspNetCore.Mvc;

namespace KameronSushi.Api.Controllers;

[ApiController]
[Route("api/kitchen/orders")]
public sealed class KitchenController(IPosStore store) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<KitchenOrderSummary>>> GetOrders(CancellationToken cancellationToken) =>
        Ok(await store.GetKitchenOrdersAsync(cancellationToken));

    [HttpGet("performance")]
    public async Task<ActionResult<IReadOnlyList<KitchenPerformanceSummary>>> GetPerformance(CancellationToken cancellationToken) =>
        Ok(await store.GetKitchenPerformanceAsync(cancellationToken));

    [HttpPatch("{orderId:long}/ready")]
    public async Task<IActionResult> MarkReady(long orderId, CancellationToken cancellationToken) =>
        await store.MarkOrderReadyAsync(orderId, cancellationToken) ? NoContent() : NotFound();
}
