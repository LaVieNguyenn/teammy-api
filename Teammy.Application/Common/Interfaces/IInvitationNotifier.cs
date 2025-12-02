using Teammy.Application.Invitations.Dtos;

namespace Teammy.Application.Common.Interfaces;

public interface IInvitationNotifier
{
    Task NotifyInvitationCreatedAsync(Guid inviteeUserId, InvitationRealtimeDto dto, CancellationToken ct);
    Task NotifyInvitationStatusAsync(Guid inviteeUserId, Guid invitationId, string status, CancellationToken ct);
    Task NotifyGroupPendingAsync(Guid groupId, CancellationToken ct);
}
