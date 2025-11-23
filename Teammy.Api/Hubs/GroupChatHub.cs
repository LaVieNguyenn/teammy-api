using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using System.Security.Claims;

namespace Teammy.Api.Hubs;

[Authorize]
public sealed class GroupChatHub : Hub
{
    public async Task JoinGroup(string groupId)
    {
        if (Guid.TryParse(groupId, out var gid))
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, GetGroupName(gid));
            var user = GetUserInfo();
            await Clients.Group(GetGroupName(gid))
                .SendAsync("PresenceChanged", new { groupId = gid, user?.UserId, user?.DisplayName, status = "joined" });
        }
    }

    public async Task LeaveGroup(string groupId)
    {
        if (Guid.TryParse(groupId, out var gid))
        {
            var user = GetUserInfo();
            await Clients.Group(GetGroupName(gid))
                .SendAsync("PresenceChanged", new { groupId = gid, user?.UserId, user?.DisplayName, status = "left" });
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, GetGroupName(gid));
        }
    }

    public async Task JoinSession(string sessionId)
    {
        if (Guid.TryParse(sessionId, out var sid))
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, GetSessionName(sid));
        }
    }

    public async Task LeaveSession(string sessionId)
    {
        if (Guid.TryParse(sessionId, out var sid))
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, GetSessionName(sid));
        }
    }

    public async Task Typing(string groupId, bool isTyping)
    {
        if (!Guid.TryParse(groupId, out var gid)) return;
        var user = GetUserInfo();
        await Clients.Group(GetGroupName(gid))
            .SendAsync("Typing", new { groupId = gid, user?.UserId, user?.DisplayName, isTyping });
    }

    internal static string GetGroupName(Guid groupId) => $"group:{groupId}";
    internal static string GetSessionName(Guid sessionId) => $"session:{sessionId}";

    private (Guid UserId, string? DisplayName)? GetUserInfo()
    {
        var sub = Context.User?.FindFirstValue(ClaimTypes.NameIdentifier) ?? Context.User?.FindFirstValue("sub");
        if (!Guid.TryParse(sub, out var userId)) return null;
        var displayName = Context.User?.FindFirstValue("name") ?? Context.User?.Identity?.Name;
        return (userId, displayName);
    }
}
