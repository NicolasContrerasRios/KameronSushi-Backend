using KameronSushi.Application.Abstractions;
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

    [HttpGet("availability")]
    public async Task<ActionResult<CustomerOrderingAvailability>> GetAvailability(CancellationToken cancellationToken) =>
        Ok(new CustomerOrderingAvailability(await store.GetCurrentShiftAsync(cancellationToken) is not null));

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
