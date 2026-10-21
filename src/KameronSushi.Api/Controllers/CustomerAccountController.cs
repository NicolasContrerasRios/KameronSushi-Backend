using KameronSushi.Application.Abstractions;
using KameronSushi.Application.Customers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace KameronSushi.Api.Controllers;

[ApiController, AllowAnonymous, Route("api/customer")]
public sealed class CustomerAccountController(ICustomerAppStore store, IGoogleTokenValidator googleTokenValidator) : ControllerBase
{
    [EnableRateLimiting("authentication"), HttpPost("auth/register")]
    public async Task<ActionResult<CustomerSession>> Register(RegisterCustomer request,CancellationToken ct)
    { try { return Ok(await store.RegisterAsync(request,ct)); } catch(ArgumentException e){ return Problem(statusCode:400,title:"No se pudo crear la cuenta",detail:e.Message); } }

    [EnableRateLimiting("authentication"), HttpPost("auth/login")]
    public async Task<ActionResult<CustomerSession>> Login(LoginCustomer request,CancellationToken ct)
    { try { var result=await store.LoginAsync(request,ct); return result is null?Problem(statusCode:401,title:"Datos incorrectos",detail:"El teléfono o PIN no coinciden."):Ok(result); } catch(ArgumentException e){ return Problem(statusCode:400,title:"Datos inválidos",detail:e.Message); } }

    [EnableRateLimiting("authentication"), HttpPost("auth/google")]
    public async Task<ActionResult<CustomerSession>> Google(LoginCustomerWithGoogle request,CancellationToken ct)
    {
        var identity=await googleTokenValidator.ValidateAsync(request.IdToken,ct);
        if(identity is null) return Problem(statusCode:401,title:"Acceso de Google inválido",detail:"Google no pudo validar esta identidad.");
        return Ok(await store.LoginWithGoogleAsync(identity,request.DeviceName,ct));
    }

    [HttpPost("auth/logout")]
    public async Task<IActionResult> Logout(CancellationToken ct){var token=Bearer();if(token is not null)await store.RevokeSessionAsync(token,ct);return NoContent();}

    [HttpGet("home")]
    public async Task<ActionResult<CustomerHome>> Home(CancellationToken ct){var profile=await Current(ct);return Ok(await store.GetHomeAsync(profile?.CustomerId,ct));}

    [HttpGet("account")]
    public async Task<ActionResult<CustomerProfile>> Account(CancellationToken ct){var profile=await Current(ct);return profile is null?Unauthorized():Ok(profile);}

    private async Task<CustomerProfile?> Current(CancellationToken ct){var token=Bearer();return token is null?null:await store.GetSessionCustomerAsync(token,ct);}
    private string? Bearer(){var value=Request.Headers.Authorization.ToString();return value.StartsWith("Bearer ",StringComparison.OrdinalIgnoreCase)?value[7..].Trim():null;}
}
