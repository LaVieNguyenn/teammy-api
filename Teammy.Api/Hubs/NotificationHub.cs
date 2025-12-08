using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Teammy.Api.Hubs;

[Authorize]
public sealed class NotificationHub : Hub
{
    public Task JoinAdminFeed()
    {
        if (Context.User?.IsInRole("admin") != true)
            throw new HubException("not_admin");
        return Groups.AddToGroupAsync(Context.ConnectionId, ActivityLogNotifier.AdminGroupName);
    }
}
