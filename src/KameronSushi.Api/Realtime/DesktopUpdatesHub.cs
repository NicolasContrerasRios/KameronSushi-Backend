using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace KameronSushi.Api.Realtime;

// Only invalidation notices; all data still comes from the authorized REST endpoints.
[Authorize(Roles = "administrador,caja,cocina")]
public sealed class DesktopUpdatesHub : Hub { }
