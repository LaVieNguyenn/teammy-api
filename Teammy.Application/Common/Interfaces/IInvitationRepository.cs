using Teammy.Application.Invitations.Dtos;

namespace Teammy.Application.Common.Interfaces;

public interface IInvitationRepository
{
    Task<Guid> CreateAsync(Guid groupId, Guid inviteeUserId, Guid invitedBy, string? message, DateTime? expiresAt, CancellationToken ct);
    Task UpdateStatusAsync(Guid invitationId, string newStatus, DateTime? respondedAt, CancellationToken ct);
    Task UpdateExpirationAsync(Guid invitationId, DateTime expiresAt, CancellationToken ct);
    Task ExpirePendingAsync(DateTime utcNow, CancellationToken ct);
}
