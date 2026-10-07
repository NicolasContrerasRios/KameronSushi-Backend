using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using KameronSushi.Application.Abstractions;
using KameronSushi.Application.Archives;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace KameronSushi.Api.Controllers;

[ApiController]
[Authorize(Roles = "administrador")]
[Route("api/admin/archives")]
public sealed class ArchivesController(IArchiveStore store) : ControllerBase
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [HttpGet("months")]
    public async Task<ActionResult<IReadOnlyList<ArchiveMonth>>> GetMonths(CancellationToken cancellationToken) =>
        Ok(await store.GetMonthsAsync(cancellationToken));

    [HttpGet("{year:int:min(2000):max(2200)}/{month:int:min(1):max(12)}")]
    public async Task<IActionResult> Download(int year, int month, CancellationToken cancellationToken)
    {
        try
        {
            var periodStart = new DateOnly(year, month, 1);
            var archive = await store.BuildArchiveAsync(periodStart, cancellationToken);
            var bytes = JsonSerializer.SerializeToUtf8Bytes(archive, JsonOptions);
            var sha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            await store.RegisterPreparedArchiveAsync(periodStart, sha256, bytes.LongLength, cancellationToken);
            Response.Headers["X-Archive-Sha256"] = sha256;
            Response.Headers["X-Archive-Schema"] = archive.SchemaVersion.ToString();
            return File(bytes, "application/json", $"kameron-sushi-{year:D4}-{month:D2}.json");
        }
        catch (ArgumentException exception)
        {
            return Problem(statusCode: 400, title: "Período inválido", detail: exception.Message);
        }
    }

    [HttpPost("{year:int:min(2000):max(2200)}/{month:int:min(1):max(12)}/confirm")]
    public async Task<ActionResult<ArchivePurgeResult>> Confirm(
        int year, int month, ConfirmArchive request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Sha256) || request.Sha256.Length != 64)
            return Problem(statusCode: 400, title: "Hash inválido", detail: "Debes confirmar el SHA-256 del archivo guardado.");
        try
        {
            return Ok(await store.ConfirmAndPurgeAsync(
                new DateOnly(year, month, 1), request.Sha256.Trim().ToLowerInvariant(), GetUserId(), cancellationToken));
        }
        catch (ArgumentException exception)
        {
            return Problem(statusCode: 400, title: "No se puede depurar", detail: exception.Message);
        }
    }

    private long GetUserId() => long.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? throw new InvalidOperationException("La sesión no contiene el usuario."));
}

public sealed record ConfirmArchive(string Sha256);
