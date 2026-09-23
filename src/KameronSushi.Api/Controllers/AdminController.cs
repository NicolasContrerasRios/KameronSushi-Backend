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

    [HttpPost("categories")]
    public Task<ActionResult<AdminCategory>> CreateCategory(SaveAdminCategory category, CancellationToken cancellationToken) =>
        SaveCategory(null, category, cancellationToken);

    [HttpPut("categories/{categoryId:long}")]
    public Task<ActionResult<AdminCategory>> UpdateCategory(long categoryId, SaveAdminCategory category, CancellationToken cancellationToken) =>
        SaveCategory(categoryId, category, cancellationToken);

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

    [HttpGet("wrappers")]
    public async Task<ActionResult<IReadOnlyList<AdminWrapper>>> GetWrappers(CancellationToken cancellationToken) =>
        Ok(await store.GetWrappersAsync(cancellationToken));

    [HttpPost("wrappers")]
    public Task<ActionResult<AdminWrapper>> CreateWrapper(SaveAdminWrapper wrapper, CancellationToken cancellationToken) =>
        SaveWrapper(null, wrapper, cancellationToken);

    [HttpPut("wrappers/{wrapperId:long}")]
    public Task<ActionResult<AdminWrapper>> UpdateWrapper(long wrapperId, SaveAdminWrapper wrapper, CancellationToken cancellationToken) =>
        SaveWrapper(wrapperId, wrapper, cancellationToken);

    [HttpGet("sauces")]
    public async Task<ActionResult<IReadOnlyList<AdminSauce>>> GetSauces(CancellationToken cancellationToken) =>
        Ok(await store.GetSaucesAsync(cancellationToken));

    [HttpPost("sauces")]
    public Task<ActionResult<AdminSauce>> CreateSauce(SaveAdminSauce sauce, CancellationToken cancellationToken) =>
        SaveSauce(null, sauce, cancellationToken);

    [HttpPut("sauces/{sauceId:long}")]
    public Task<ActionResult<AdminSauce>> UpdateSauce(long sauceId, SaveAdminSauce sauce, CancellationToken cancellationToken) =>
        SaveSauce(sauceId, sauce, cancellationToken);

    [HttpGet("products/{productId:long}/configuration")]
    public async Task<ActionResult<AdminProductConfiguration>> GetProductConfiguration(long productId, CancellationToken cancellationToken)
    {
        var result = await store.GetProductConfigurationAsync(productId, cancellationToken);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpPut("products/{productId:long}/configuration")]
    public async Task<ActionResult<AdminProductConfiguration>> SaveProductConfiguration(
        long productId, SaveAdminProductConfiguration configuration, CancellationToken cancellationToken)
    {
        try
        {
            var result = await store.SaveProductConfigurationAsync(productId, configuration, cancellationToken);
            return result is null ? NotFound() : Ok(result);
        }
        catch (ArgumentException exception) { return BadRequestProblem(exception); }
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

    private async Task<ActionResult<AdminCategory>> SaveCategory(long? id, SaveAdminCategory category, CancellationToken token)
    {
        try { return Ok(await store.SaveCategoryAsync(id, category, token)); }
        catch (ArgumentException exception) { return BadRequestProblem(exception); }
    }

    private async Task<ActionResult<AdminWrapper>> SaveWrapper(long? id, SaveAdminWrapper wrapper, CancellationToken token)
    {
        try { return Ok(await store.SaveWrapperAsync(id, wrapper, token)); }
        catch (ArgumentException exception) { return BadRequestProblem(exception); }
    }

    private async Task<ActionResult<AdminSauce>> SaveSauce(long? id, SaveAdminSauce sauce, CancellationToken token)
    {
        try { return Ok(await store.SaveSauceAsync(id, sauce, token)); }
        catch (ArgumentException exception) { return BadRequestProblem(exception); }
    }
}
