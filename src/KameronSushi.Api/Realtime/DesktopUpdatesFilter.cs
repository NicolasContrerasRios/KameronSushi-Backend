using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.SignalR;

namespace KameronSushi.Api.Realtime;

public sealed class DesktopUpdatesFilter(
    IHubContext<DesktopUpdatesHub> hub,
    ILogger<DesktopUpdatesFilter> logger) : IAsyncResultFilter
{
    public static string? GetScope(string method, string path)
    {
        if (method is not ("POST" or "PUT" or "PATCH" or "DELETE")) return null;
        if (path.StartsWith("/api/admin/", StringComparison.OrdinalIgnoreCase)) return "admin";
        if (path.StartsWith("/api/pos/shifts/", StringComparison.OrdinalIgnoreCase)) return "shift";
        if (path.StartsWith("/api/pos/orders", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/api/kitchen/orders/", StringComparison.OrdinalIgnoreCase) ||
            path.Equals("/api/customer/orders", StringComparison.OrdinalIgnoreCase)) return "orders";
        return null;
    }

    public async Task OnResultExecutionAsync(ResultExecutingContext context, ResultExecutionDelegate next)
    {
        var executed = await next();
        var response = context.HttpContext.Response;
        var scope = GetScope(context.HttpContext.Request.Method, context.HttpContext.Request.Path.Value ?? "");
        if (executed.Canceled || executed.Exception is not null || response.StatusCode is < 200 or >= 300 || scope is null) return;
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            await hub.Clients.All.SendAsync("DataChanged", scope, timeout.Token);
        }
        catch (Exception exception)
        {
            // A notification failure must never turn a committed sale into a failed sale.
            logger.LogWarning(exception, "No se pudo emitir el aviso de actualización {Scope}", scope);
        }
    }
}
