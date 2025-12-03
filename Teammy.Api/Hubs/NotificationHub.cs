using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Teammy.Api.Hubs;

[Authorize]
public sealed class NotificationHub : Hub
{
    // Clients connect only to receive server-pushed notifications.
}
