using KameronSushi.Application.Abstractions;
using KameronSushi.Application.Admin;
using Microsoft.AspNetCore.Mvc;

namespace KameronSushi.Api.Controllers;

[ApiController]
[Route("api/admin")]
public sealed class AdminController(IAdminStore store) : ControllerBase
{
    [HttpGet("categories")]
    public async Task<ActionResult<IReadOnlyList<AdminCategory>>> GetCategories(CancellationToken cancellationToken) =>
        Ok(await store.GetCategoriesAsync(cancellationToken));

    [HttpGet("products")]
    public async Task<ActionResult<IReadOnlyList<AdminProduct>>> GetProducts(CancellationToken cancellationToken) =>
        Ok(await store.GetProductsAsync(cancellationToken));

    [HttpPost("products")]
    public async Task<ActionResult<AdminProduct>> CreateProduct(
        SaveAdminProduct product, CancellationToken cancellationToken)
    {
        try
        {
            var created = await store.CreateProductAsync(product, cancellationToken);
            return Created($"/api/admin/products/{created.Id}", created);
        }
        catch (ArgumentException exception)
        {
            return BadRequestProblem(exception);
        }
    }

    [HttpPut("products/{productId:long}")]
    public async Task<ActionResult<AdminProduct>> UpdateProduct(
        long productId, SaveAdminProduct product, CancellationToken cancellationToken)
    {
        try
        {
            var updated = await store.UpdateProductAsync(productId, product, cancellationToken);
            return updated is null ? NotFound() : Ok(updated);
        }
        catch (ArgumentException exception)
        {
            return BadRequestProblem(exception);
        }
    }

    [HttpGet("rewards")]
    public async Task<ActionResult<IReadOnlyList<AdminRewardProduct>>> GetRewards(CancellationToken cancellationToken) =>
        Ok(await store.GetRewardsAsync(cancellationToken));

    [HttpPost("rewards")]
    public async Task<ActionResult<AdminRewardProduct>> SaveReward(
        SaveAdminRewardProduct reward, CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await store.SaveRewardAsync(reward, cancellationToken));
        }
        catch (ArgumentException exception)
        {
            return BadRequestProblem(exception);
        }
    }

    [HttpDelete("rewards/{rewardProductId:long}")]
    public async Task<IActionResult> DisableReward(long rewardProductId, CancellationToken cancellationToken) =>
        await store.DisableRewardAsync(rewardProductId, cancellationToken) ? NoContent() : NotFound();

    private ObjectResult BadRequestProblem(ArgumentException exception) => Problem(
        statusCode: StatusCodes.Status400BadRequest,
        title: "Datos inválidos",
        detail: exception.Message);
}
