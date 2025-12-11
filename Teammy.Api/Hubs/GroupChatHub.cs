using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using System.Security.Claims;

namespace Teammy.Api.Hubs;

[Authorize]
public sealed class GroupChatHub(IChatPresenceTracker presenceTracker, ILogger<GroupChatHub> logger) : Hub
{
    private readonly IChatPresenceTracker _presenceTracker = presenceTracker;
    private readonly ILogger<GroupChatHub> _logger = logger;

    public override async Task OnConnectedAsync()
    {
        try
        {
            _logger.LogInformation("✅ OnConnectedAsync: ConnectionId={ConnectionId}, User={User}", 
                Context.ConnectionId, Context.User?.Identity?.Name ?? "Anonymous");
            await base.OnConnectedAsync();
            
            var user = GetUserInfo();
            if (user != null)
            {
                _logger.LogInformation("🟢 Broadcasting UserOnline: UserId={UserId}, DisplayName={DisplayName}", 
                    user.Value.UserId, user.Value.DisplayName);
                await Clients.All.SendAsync("UserOnline", new 
                { 
                    userId = user.Value.UserId, 
                    displayName = user.Value.DisplayName 
                });
            }
            
            _logger.LogInformation("✅ OnConnectedAsync completed for {ConnectionId}", Context.ConnectionId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ OnConnectedAsync failed: {Message}", ex.Message);
            throw;
        }
    }

    public async Task JoinGroup(string groupId)
    {
        try
        {
            _logger.LogInformation("📍 JoinGroup called: groupId={GroupId}, connectionId={ConnectionId}", groupId, Context.ConnectionId);
            if (Guid.TryParse(groupId, out var gid))
            {
                await Groups.AddToGroupAsync(Context.ConnectionId, GetGroupName(gid));
                var user = GetUserInfo();
                _logger.LogInformation("✅ JoinGroup: User {UserId} joined group {GroupId}", user?.UserId, gid);
                await Clients.Group(GetGroupName(gid))
                    .SendAsync("PresenceChanged", new { groupId = gid, user?.UserId, user?.DisplayName, status = "joined" });
            }
            else
            {
                _logger.LogWarning("⚠️ JoinGroup: Invalid groupId format: {GroupId}", groupId);
            }
        }  
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ JoinGroup failed: {Message}", ex.Message);
            throw;
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
        try
        {
            _logger.LogInformation("📍 JoinSession called: sessionId={SessionId}, connectionId={ConnectionId}", sessionId, Context.ConnectionId);
            
            if (!Guid.TryParse(sessionId, out var sid))
            {
                _logger.LogWarning("⚠️ JoinSession: Invalid sessionId format: {SessionId}", sessionId);
                return;
            }
            
            var user = GetUserInfo();
            if (user is null)
            {
                _logger.LogWarning("⚠️ JoinSession: User info not found for connectionId={ConnectionId}", Context.ConnectionId);
                return;
            }

            await Groups.AddToGroupAsync(Context.ConnectionId, GetSessionName(sid));
            var snapshot = _presenceTracker.AddSessionConnection(sid, user.Value.UserId, user.Value.DisplayName, Context.ConnectionId);

            _logger.LogInformation("✅ JoinSession: User {UserId} ({DisplayName}) joined session {SessionId}, snapshot has {Count} users", 
                user.Value.UserId, user.Value.DisplayName, sid, snapshot.Count);

            await Clients.Caller.SendAsync("SessionPresenceSnapshot", new { sessionId = sid, users = snapshot });
            await Clients.GroupExcept(GetSessionName(sid), new[] { Context.ConnectionId })
                .SendAsync("SessionPresenceChanged", new { sessionId = sid, userId = user.Value.UserId, displayName = user.Value.DisplayName, status = "joined" });
            
            _logger.LogInformation("✅ JoinSession: Events sent successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ JoinSession failed: {Message}", ex.Message);
            throw;
        }
    }

    public async Task LeaveSession(string sessionId)
    {
        try
        {
            _logger.LogInformation("📍 LeaveSession called: sessionId={SessionId}, connectionId={ConnectionId}", sessionId, Context.ConnectionId);
            
            if (!Guid.TryParse(sessionId, out var sid)) return;
            var user = GetUserInfo();
            if (user is null) return;

            if (_presenceTracker.TryRemoveSessionConnection(sid, user.Value.UserId, Context.ConnectionId, out var displayName, out var userRemoved) && userRemoved)
            {
                _logger.LogInformation("✅ LeaveSession: User {UserId} left session {SessionId}", user.Value.UserId, sid);
                await Clients.Group(GetSessionName(sid))
                    .SendAsync("SessionPresenceChanged", new { sessionId = sid, userId = user.Value.UserId, displayName, status = "left" });
            }

            await Groups.RemoveFromGroupAsync(Context.ConnectionId, GetSessionName(sid));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ LeaveSession failed: {Message}", ex.Message);
            throw;
        }
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
        try
        {
            _logger.LogInformation("🔴 OnDisconnectedAsync: ConnectionId={ConnectionId}, Exception={Exception}", 
                Context.ConnectionId, exception?.Message ?? "None");
            
            var user = GetUserInfo();
            if (user != null)
            {
                _logger.LogInformation("🔴 Broadcasting UserOffline: UserId={UserId}, DisplayName={DisplayName}", 
                    user.Value.UserId, user.Value.DisplayName);
                await Clients.All.SendAsync("UserOffline", new 
                { 
                    userId = user.Value.UserId, 
                    displayName = user.Value.DisplayName 
                });
            }
            
            var removals = _presenceTracker.RemoveConnection(Context.ConnectionId);
            _logger.LogInformation("✅ OnDisconnectedAsync: Removed {Count} session connections", removals.Count);
            
            foreach (var (sessionId, userId, displayName) in removals)
            {
                await Clients.Group(GetSessionName(sessionId))
                    .SendAsync("SessionPresenceChanged", new { sessionId, userId, displayName, status = "left" });
            }

            await base.OnDisconnectedAsync(exception);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ OnDisconnectedAsync failed: {Message}", ex.Message);
            throw;
        }
    }
}
