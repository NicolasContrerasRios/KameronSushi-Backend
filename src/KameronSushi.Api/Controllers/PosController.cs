using KameronSushi.Application.Abstractions;
using KameronSushi.Application.Pos;
using Microsoft.AspNetCore.Mvc;

namespace KameronSushi.Api.Controllers;

[ApiController]
[Route("api/pos")]
public sealed class PosController(IPosStore store) : ControllerBase
{
    private static readonly HashSet<string> ValidStatuses =
    [
        "borrador", "pendiente_pago", "confirmado", "en_preparacion",
        "listo", "en_reparto", "entregado", "cancelado"
    ];

    [HttpGet("catalog")]
    public async Task<ActionResult<IReadOnlyList<CatalogProduct>>> GetCatalog(CancellationToken cancellationToken) =>
        Ok(await store.GetCatalogAsync(cancellationToken));

    [HttpPost("orders")]
    public async Task<ActionResult<CreatedOrder>> CreateOrder(
        [FromBody] CreateLocalOrder command,
        CancellationToken cancellationToken)
    {
        try
        {
            var created = await store.CreateLocalOrderAsync(command, cancellationToken);
            return CreatedAtAction(nameof(GetOrder), new { orderId = created.OrderId }, created);
        }
        catch (ArgumentException exception)
        {
            return Problem(statusCode: StatusCodes.Status400BadRequest, title: "Pedido inválido", detail: exception.Message);
        }
    }

    [HttpGet("orders")]
    public async Task<ActionResult<IReadOnlyList<PosOrderSummary>>> GetOrders(
        [FromQuery] string? status,
        [FromQuery] int limit = 50,
        CancellationToken cancellationToken = default)
    {
        var normalizedStatus = string.IsNullOrWhiteSpace(status) ? null : status.Trim().ToLowerInvariant();
        if (normalizedStatus is not null && !ValidStatuses.Contains(normalizedStatus))
        {
            return Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Estado inválido",
                detail: $"El estado '{status}' no existe.");
        }

        if (limit is < 1 or > 200)
        {
            return Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Límite inválido",
                detail: "El límite debe estar entre 1 y 200.");
        }

        return Ok(await store.GetOrdersAsync(normalizedStatus, limit, cancellationToken));
    }

    [HttpGet("orders/{orderId:long}")]
    public async Task<ActionResult<PosOrderDetails>> GetOrder(long orderId, CancellationToken cancellationToken)
    {
        var order = await store.GetOrderAsync(orderId, cancellationToken);
        return order is null ? NotFound() : Ok(order);
    }
}
