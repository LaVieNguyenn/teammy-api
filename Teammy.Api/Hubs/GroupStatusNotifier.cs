using Microsoft.AspNetCore.SignalR;
using Teammy.Application.Common.Interfaces;

namespace Teammy.Api.Hubs;

public sealed class GroupStatusNotifier(IHubContext<GroupChatHub> hubContext) : IGroupStatusNotifier
{
    private readonly IHubContext<GroupChatHub> _hubContext = hubContext;

    public Task NotifyGroupStatusAsync(Guid groupId, Guid userId, string status, string action, CancellationToken ct)
        => _hubContext.Clients.User(userId.ToString())
            .SendAsync("GroupStatusChanged", new { groupId, status, action }, cancellationToken: ct);
}
