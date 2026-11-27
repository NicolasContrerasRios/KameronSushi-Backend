using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Threading.Channels;
using KameronSushi.Api.Realtime;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace KameronSushi.Tests;

public sealed class DesktopRealtimeTests
{
    [Theory]
    [InlineData("POST", "/api/customer/orders", "orders")]
    [InlineData("POST", "/api/pos/orders", "orders")]
    [InlineData("PATCH", "/api/kitchen/orders/1/ready", "orders")]
    [InlineData("POST", "/api/pos/orders/1/cancel", "orders")]
    [InlineData("PUT", "/api/pos/shifts/1/estimated-wait", "shift")]
    [InlineData("PUT", "/api/admin/products/1", "admin")]
    [InlineData("GET", "/api/pos/orders", null)]
    [InlineData("POST", "/api/kitchen/print-jobs/claim", null)]
    [InlineData("POST", "/api/auth/login", null)]
    public void OnlyBusinessMutationsInvalidateData(string method, string path, string? expected) =>
        Assert.Equal(expected, DesktopUpdatesFilter.GetScope(method, path));

    [Fact]
    public async Task HubRequiresStaffAndBroadcastsOnlySuccessfulChanges()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddLogging();
        builder.Services.AddAuthentication("test")
            .AddScheme<AuthenticationSchemeOptions, RealtimeTestAuth>("test", _ => { });
        builder.Services.AddAuthorization();
        builder.Services.AddSignalR();
        builder.Services.AddControllers(options => options.Filters.Add<DesktopUpdatesFilter>())
            .ConfigureApplicationPartManager(manager =>
            {
                manager.ApplicationParts.Clear();
                manager.ApplicationParts.Add(new AssemblyPart(typeof(RealtimeTestController).Assembly));
            });
        await using var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapControllers();
        app.MapHub<DesktopUpdatesHub>("/hubs/desktop-updates");
        await app.StartAsync();
        var server = app.GetTestServer();
        HubConnection Client(string? token) => new HubConnectionBuilder()
            .WithUrl("http://localhost/hubs/desktop-updates", options =>
            {
                options.Transports = HttpTransportType.LongPolling;
                options.HttpMessageHandlerFactory = _ => server.CreateHandler();
                options.AccessTokenProvider = () => Task.FromResult(token);
            }).Build();

        await using var anonymous = Client(null);
        await Assert.ThrowsAsync<HttpRequestException>(() => anonymous.StartAsync());
        await using var nonStaff = Client("customer");
        await Assert.ThrowsAsync<HttpRequestException>(() => nonStaff.StartAsync());
        await using var first = Client("staff");
        await using var second = Client("staff");
        var firstMessages = Channel.CreateUnbounded<string>();
        var secondMessages = Channel.CreateUnbounded<string>();
        first.On<string>("DataChanged", value => firstMessages.Writer.TryWrite(value));
        second.On<string>("DataChanged", value => secondMessages.Writer.TryWrite(value));
        await first.StartAsync();
        await second.StartAsync();
        using var http = server.CreateClient();
        using var failure = await http.PostAsync("/api/pos/orders/fail", null);
        Assert.Equal(HttpStatusCode.BadRequest, failure.StatusCode);
        using var success = await http.PostAsync("/api/pos/orders", null);
        Assert.Equal(HttpStatusCode.OK, success.StatusCode);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        Assert.Equal("orders", await firstMessages.Reader.ReadAsync(timeout.Token));
        Assert.Equal("orders", await secondMessages.Reader.ReadAsync(timeout.Token));
        Assert.False(firstMessages.Reader.TryRead(out _));
        Assert.False(secondMessages.Reader.TryRead(out _));
    }
}

[ApiController, Route("api/pos/orders")]
public sealed class RealtimeTestController : ControllerBase
{
    [HttpPost] public IActionResult Success() => Ok();
    [HttpPost("fail")] public IActionResult Failure() => BadRequest();
}

public sealed class RealtimeTestAuth(
    IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var token = Request.Headers.Authorization.ToString();
        if (token != "Bearer staff" && token != "Bearer customer")
            return Task.FromResult(AuthenticateResult.NoResult());
        var identity = new ClaimsIdentity([
            new Claim(ClaimTypes.NameIdentifier, "test-user"),
            new Claim(ClaimTypes.Role, token == "Bearer staff" ? "caja" : "cliente")
        ], "test");
        return Task.FromResult(AuthenticateResult.Success(
            new AuthenticationTicket(new ClaimsPrincipal(identity), "test")));
    }
}
