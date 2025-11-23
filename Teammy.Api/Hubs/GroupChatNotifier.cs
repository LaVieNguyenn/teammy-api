using Microsoft.AspNetCore.SignalR;
using Teammy.Api.Hubs;
using Teammy.Application.Chat.Dtos;
using Teammy.Application.Common.Interfaces;

namespace Teammy.Api.Hubs;

public sealed class GroupChatNotifier(IHubContext<GroupChatHub> hubContext) : IGroupChatNotifier
{
    private readonly IHubContext<GroupChatHub> _hubContext = hubContext;

    public Task NotifyMessageAsync(Guid groupId, ChatMessageDto message, CancellationToken ct)
    {
        return _hubContext.Clients.Group(GroupChatHub.GetGroupName(groupId))
            .SendAsync("ReceiveMessage", message, cancellationToken: ct);
    }
    public Task NotifySessionAsync(Guid sessionId, ChatMessageDto message, CancellationToken ct)
    {
        return _hubContext.Clients.Group(GroupChatHub.GetSessionName(sessionId))
            .SendAsync("ReceiveMessage", message, cancellationToken: ct);
    }
}
