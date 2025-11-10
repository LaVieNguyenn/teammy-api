using Teammy.Application.Invitations.Dtos;

namespace Teammy.Application.Common.Interfaces;

public interface IInvitationReadOnlyQueries
{
    Task<InvitationDetailDto?> GetAsync(Guid invitationId, CancellationToken ct);
    Task<IReadOnlyList<InvitationListItemDto>> ListForUserAsync(Guid userId, string? status, CancellationToken ct);

    // Check for duplicate pending invitation for same post + invitee
    Task<Guid?> FindPendingIdAsync(Guid postId, Guid inviteeUserId, CancellationToken ct);

    // Check if any invitation exists for (postId, invitee) and return id + status
    Task<(Guid InvitationId, string Status)?> FindAnyAsync(Guid postId, Guid inviteeUserId, CancellationToken ct);
}
