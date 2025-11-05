using Teammy.Application.Invitations.Dtos;

namespace Teammy.Application.Common.Interfaces;

public interface IInvitationRepository
{
    Task<Guid> CreateAsync(Guid postId, Guid inviteeUserId, Guid invitedBy, string? message, DateTime? expiresAt, CancellationToken ct);
    Task UpdateStatusAsync(Guid invitationId, string newStatus, DateTime? respondedAt, CancellationToken ct);
}

