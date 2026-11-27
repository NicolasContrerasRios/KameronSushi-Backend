using KameronSushi.Application.Abstractions;
using KameronSushi.Application.Customers;
using KameronSushi.Application.Pos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace KameronSushi.Api.Controllers;

[ApiController]
[AllowAnonymous]
[Route("api/customer")]
public sealed class CustomerOrderingController(IPosStore store, ICustomerAppStore customerStore) : ControllerBase
{
    [HttpGet("catalog")]
    public async Task<ActionResult<IReadOnlyList<CatalogProduct>>> GetCatalog(CancellationToken cancellationToken) =>
        Ok(await store.GetCatalogAsync(cancellationToken));

    [HttpGet("complements")]
    public async Task<ActionResult<IReadOnlyList<ComplementRecommendation>>> GetComplements(
        [FromQuery] long[]? productIds, [FromQuery] long[]? candidateIds, CancellationToken cancellationToken)
    {
        productIds ??= [];
        candidateIds ??= [];
        if (productIds.Length > 20 || candidateIds.Length > 60 || productIds.Concat(candidateIds).Any(id => id <= 0))
            return BadRequest(new ProblemDetails { Status = 400, Title = "La selección de productos no es válida." });
        return Ok(await store.GetComplementRecommendationsAsync(productIds.Distinct().ToArray(), candidateIds.Distinct().ToArray(), cancellationToken));
    }

    [HttpGet("availability")]
    public async Task<ActionResult<CustomerOrderingAvailability>> GetAvailability(CancellationToken cancellationToken)
    {
        var shift = await store.GetCurrentShiftAsync(cancellationToken);
        return Ok(new CustomerOrderingAvailability(
            shift is not null,
            shift?.EstimatedWaitMinMinutes,
            shift?.EstimatedWaitMaxMinutes));
    }

    [HttpGet("orders")]
    public async Task<ActionResult<IReadOnlyList<CustomerOrderSummary>>> GetOrders(
        [FromQuery] int limit = 50,
        CancellationToken cancellationToken = default)
    {
        var authorization = Request.Headers.Authorization.ToString();
        var token = authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
            ? authorization[7..].Trim()
            : null;
        if (string.IsNullOrWhiteSpace(token))
            return Unauthorized();

        var customer = await customerStore.GetSessionCustomerAsync(token, cancellationToken);
        if (customer is null)
            return Unauthorized();

        return Ok(await customerStore.GetOrdersAsync(customer.CustomerId, limit, cancellationToken));
    }

    [EnableRateLimiting("customer-orders")]
    [HttpPost("orders")]
    public async Task<ActionResult<CreatedOrder>> CreateOrder(
        CreateCustomerOrder request,
        CancellationToken cancellationToken)
    {
        try
        {
            var authorization = Request.Headers.Authorization.ToString();
            var token = authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ? authorization[7..].Trim() : null;
            if (string.IsNullOrWhiteSpace(token))
            {
                return Problem(
                    statusCode: StatusCodes.Status401Unauthorized,
                    title: "Sesión requerida",
                    detail: "Inicia sesión en la aplicación antes de confirmar el pedido.");
            }

            var customer = await customerStore.GetSessionCustomerAsync(token, cancellationToken);
            if (customer is null)
            {
                return Problem(
                    statusCode: StatusCodes.Status401Unauthorized,
                    title: "Sesión inválida o vencida",
                    detail: "Vuelve a iniciar sesión antes de confirmar el pedido.");
            }

            var created = await store.CreateCustomerOrderAsync(request, customer.CustomerId, cancellationToken);
            return Created($"/api/customer/orders/{created.OrderId}", created);
        }
        catch (ArgumentException exception) when (exception.Message.StartsWith("LOCAL_CLOSED:", StringComparison.Ordinal))
        {
            return Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Local cerrado",
                detail: exception.Message["LOCAL_CLOSED:".Length..].Trim());
        }
        catch (ArgumentException exception)
        {
            return Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Pedido inválido",
                detail: exception.Message);
        }
    }
}
