using KameronSushi.Application.Abstractions;
using KameronSushi.Application.Pos;
using Microsoft.AspNetCore.Mvc;

namespace KameronSushi.Api.Controllers;

[ApiController]
[Route("api/kitchen/print-jobs")]
public sealed class KitchenPrintController(IPosStore store) : ControllerBase
{
    [HttpPost("claim")]
    public async Task<ActionResult<KitchenPrintJob>> Claim(
        ClaimKitchenPrintJob request,
        CancellationToken cancellationToken)
    {
        try
        {
            var job = await store.ClaimKitchenPrintJobAsync(request.WorkerId, cancellationToken);
            return job is null ? NoContent() : Ok(job);
        }
        catch (ArgumentException exception)
        {
            return InvalidRequest(exception);
        }
    }

    [HttpPost("{claimToken:guid}/complete")]
    public async Task<IActionResult> Complete(
        Guid claimToken,
        CompleteKitchenPrintJob request,
        CancellationToken cancellationToken)
    {
        try
        {
            return await store.CompleteKitchenPrintJobAsync(claimToken, request.WorkerId, cancellationToken)
                ? NoContent()
                : NotFound();
        }
        catch (ArgumentException exception)
        {
            return InvalidRequest(exception);
        }
    }

    [HttpPost("{claimToken:guid}/fail")]
    public async Task<IActionResult> Fail(
        Guid claimToken,
        FailKitchenPrintJob request,
        CancellationToken cancellationToken)
    {
        try
        {
            return await store.FailKitchenPrintJobAsync(claimToken, request.WorkerId, request.Error, cancellationToken)
                ? NoContent()
                : NotFound();
        }
        catch (ArgumentException exception)
        {
            return InvalidRequest(exception);
        }
    }

    private ObjectResult InvalidRequest(ArgumentException exception) => Problem(
        statusCode: StatusCodes.Status400BadRequest,
        title: "Solicitud de impresión inválida",
        detail: exception.Message);
}
