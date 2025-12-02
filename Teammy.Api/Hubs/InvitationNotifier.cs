using Microsoft.AspNetCore.SignalR;
using Teammy.Application.Common.Interfaces;
using Teammy.Application.Invitations.Dtos;

namespace Teammy.Api.Hubs;

public sealed class InvitationNotifier(IHubContext<GroupChatHub> hubContext) : IInvitationNotifier
{
    private readonly IHubContext<GroupChatHub> _hubContext = hubContext;

    public Task NotifyInvitationCreatedAsync(Guid inviteeUserId, InvitationRealtimeDto dto, CancellationToken ct)
        => _hubContext.Clients.User(inviteeUserId.ToString())
            .SendAsync("InvitationCreated", dto, cancellationToken: ct);

    public Task NotifyInvitationStatusAsync(Guid inviteeUserId, Guid invitationId, string status, CancellationToken ct)
        => _hubContext.Clients.User(inviteeUserId.ToString())
            .SendAsync("InvitationStatusChanged", new { invitationId, status }, cancellationToken: ct);

    public Task NotifyGroupPendingAsync(Guid groupId, CancellationToken ct)
        => _hubContext.Clients.Group(GroupChatHub.GetGroupName(groupId))
            .SendAsync("PendingUpdated", new { groupId }, cancellationToken: ct);
}
