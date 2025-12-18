using Teammy.Application.Invitations.Dtos;

namespace Teammy.Application.Common.Interfaces;

public interface IInvitationReadOnlyQueries
{
    Task<InvitationDetailDto?> GetAsync(Guid invitationId, CancellationToken ct);
    Task<IReadOnlyList<InvitationListItemDto>> ListForUserAsync(Guid userId, string? status, CancellationToken ct);

    // Check for duplicate pending invitation for same group + invitee
    Task<Guid?> FindPendingIdAsync(Guid groupId, Guid inviteeUserId, CancellationToken ct);

    // Check if any invitation exists for (groupId, invitee) and return id + status
    Task<(Guid InvitationId, string Status, Guid? TopicId)?> FindAnyAsync(Guid groupId, Guid inviteeUserId, CancellationToken ct);

    // Check if group already has a pending mentor invite (topic)
    Task<Guid?> GetPendingMentorTopicAsync(Guid groupId, CancellationToken ct);
}
