using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using System.Security.Claims;

namespace Teammy.Api.Hubs;

[Authorize]
public sealed class GroupChatHub(IChatPresenceTracker presenceTracker) : Hub
{
    private readonly IChatPresenceTracker _presenceTracker = presenceTracker;

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
        if (!Guid.TryParse(sessionId, out var sid)) return;
        var user = GetUserInfo();
        if (user is null) return;

        await Groups.AddToGroupAsync(Context.ConnectionId, GetSessionName(sid));
        var snapshot = _presenceTracker.AddSessionConnection(sid, user.Value.UserId, user.Value.DisplayName, Context.ConnectionId);

        await Clients.Caller.SendAsync("SessionPresenceSnapshot", new { sessionId = sid, users = snapshot });
        await Clients.GroupExcept(GetSessionName(sid), new[] { Context.ConnectionId })
            .SendAsync("SessionPresenceChanged", new { sessionId = sid, userId = user.Value.UserId, displayName = user.Value.DisplayName, status = "joined" });
    }

    public async Task LeaveSession(string sessionId)
    {
        if (!Guid.TryParse(sessionId, out var sid)) return;
        var user = GetUserInfo();
        if (user is null) return;

        if (_presenceTracker.TryRemoveSessionConnection(sid, user.Value.UserId, Context.ConnectionId, out var displayName, out var userRemoved) && userRemoved)
        {
            await Clients.Group(GetSessionName(sid))
                .SendAsync("SessionPresenceChanged", new { sessionId = sid, userId = user.Value.UserId, displayName, status = "left" });
        }

        await Groups.RemoveFromGroupAsync(Context.ConnectionId, GetSessionName(sid));
    }

    public async Task TypingSession(string sessionId, bool isTyping)
    {
        if (!Guid.TryParse(sessionId, out var sid)) return;
        var user = GetUserInfo();
        await Clients.Group(GetSessionName(sid))
            .SendAsync("TypingSession", new { sessionId = sid, user?.UserId, user?.DisplayName, isTyping });
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

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var removals = _presenceTracker.RemoveConnection(Context.ConnectionId);
        foreach (var (sessionId, userId, displayName) in removals)
        {
            await Clients.Group(GetSessionName(sessionId))
                .SendAsync("SessionPresenceChanged", new { sessionId, userId, displayName, status = "left" });
        }

        await base.OnDisconnectedAsync(exception);
    }
}
