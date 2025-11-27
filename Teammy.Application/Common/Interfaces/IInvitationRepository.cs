using Teammy.Application.Invitations.Dtos;

namespace Teammy.Application.Common.Interfaces;

public interface IInvitationRepository
{
    Task<Guid> CreateAsync(Guid groupId, Guid inviteeUserId, Guid invitedBy, string? message, DateTime? expiresAt, Guid? topicId, CancellationToken ct);
    Task UpdateStatusAsync(Guid invitationId, string newStatus, DateTime? respondedAt, CancellationToken ct);
    Task UpdateExpirationAsync(Guid invitationId, DateTime expiresAt, CancellationToken ct);
    Task ExpirePendingAsync(DateTime utcNow, CancellationToken ct);
    Task ResetPendingAsync(Guid invitationId, DateTime newCreatedAt, DateTime expiresAt, CancellationToken ct);
    Task<int> RevokePendingMentorInvitesAsync(Guid groupId, Guid exceptInvitationId, CancellationToken ct);
}
