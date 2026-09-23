using KameronSushi.Application.Abstractions;
using KameronSushi.Application.Pos;
using Microsoft.AspNetCore.Mvc;

namespace KameronSushi.Api.Controllers;

[ApiController]
[Route("api/pos")]
public sealed class PosController(IPosStore store) : ControllerBase
{
    [HttpGet("shifts/current")]
    public async Task<ActionResult<CashShift>> GetCurrentShift(CancellationToken cancellationToken)
    {
        var shift = await store.GetCurrentShiftAsync(cancellationToken);
        return shift is null ? NoContent() : Ok(shift);
    }

    [HttpPost("shifts/open")]
    public async Task<ActionResult<CashShift>> OpenShift(OpenCashShift request, CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await store.OpenShiftAsync(request.OpeningAmount, cancellationToken));
        }
        catch (ArgumentException exception)
        {
            return CashOperationProblem(exception);
        }
    }

    [HttpGet("shifts/{shiftId:long}/report")]
    public async Task<ActionResult<CashShiftReport>> GetShiftReport(long shiftId, CancellationToken cancellationToken)
    {
        var report = await store.GetShiftReportAsync(shiftId, cancellationToken);
        return report is null ? NotFound() : Ok(report);
    }

    [HttpPost("shifts/{shiftId:long}/movements")]
    public async Task<ActionResult<CashMovement>> AddCashMovement(
        long shiftId, CreateCashMovement request, CancellationToken cancellationToken)
    {
        try
        {
            var movement = await store.AddCashMovementAsync(shiftId, request, cancellationToken);
            return movement is null ? NotFound() : Ok(movement);
        }
        catch (ArgumentException exception)
        {
            return CashOperationProblem(exception);
        }
    }

    [HttpPost("shifts/{shiftId:long}/close")]
    public async Task<ActionResult<CashShiftReport>> CloseShift(
        long shiftId, CloseCashShift request, CancellationToken cancellationToken)
    {
        try
        {
            var report = await store.CloseShiftAsync(shiftId, request, cancellationToken);
            return report is null ? NotFound() : Ok(report);
        }
        catch (ArgumentException exception)
        {
            return CashOperationProblem(exception);
        }
    }

    private static readonly HashSet<string> ValidStatuses =
    [
        "borrador", "pendiente_pago", "confirmado", "en_preparacion",
        "listo", "en_reparto", "entregado", "cancelado"
    ];

    [HttpGet("catalog")]
    public async Task<ActionResult<IReadOnlyList<CatalogProduct>>> GetCatalog(CancellationToken cancellationToken) =>
        Ok(await store.GetCatalogAsync(cancellationToken));

    [HttpGet("customers/by-phone/{phone}/loyalty")]
    public async Task<ActionResult<CustomerLoyalty>> GetCustomerLoyalty(
        string phone, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(phone)) return BadRequest();
        var customer = await store.GetCustomerLoyaltyByPhoneAsync(phone, cancellationToken);
        return customer is null ? NotFound() : Ok(customer);
    }

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

    private ObjectResult CashOperationProblem(ArgumentException exception) => Problem(
        statusCode: StatusCodes.Status400BadRequest,
        title: "Operación de caja inválida",
        detail: exception.Message);
}
