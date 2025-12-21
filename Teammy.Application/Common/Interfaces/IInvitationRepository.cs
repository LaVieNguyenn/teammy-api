using Teammy.Application.Invitations.Dtos;

namespace Teammy.Application.Common.Interfaces;

public interface IInvitationRepository
{
    Task<Guid> CreateAsync(Guid groupId, Guid inviteeUserId, Guid invitedBy, string? message, DateTime? expiresAt, Guid? topicId, CancellationToken ct);
    Task UpdateStatusAsync(Guid invitationId, string newStatus, DateTime? respondedAt, CancellationToken ct);
    Task UpdateExpirationAsync(Guid invitationId, DateTime expiresAt, CancellationToken ct);
    Task<IReadOnlyList<(Guid InvitationId, Guid GroupId, Guid? TopicId)>> ExpirePendingAsync(DateTime utcNow, CancellationToken ct);
    Task ResetPendingAsync(Guid invitationId, DateTime newCreatedAt, DateTime expiresAt, CancellationToken ct);
    Task MarkMentorAwaitingLeaderAsync(Guid invitationId, DateTime respondedAt, CancellationToken ct);
    Task<int> RevokePendingMentorInvitesAsync(Guid groupId, Guid exceptInvitationId, CancellationToken ct);
    Task<IReadOnlyList<(Guid InvitationId, Guid GroupId)>> RevokePendingForUserInSemesterAsync(Guid userId, Guid semesterId, Guid? exceptInvitationId, CancellationToken ct);
    Task<IReadOnlyList<(Guid InvitationId, Guid InviteeUserId, Guid GroupId, Guid InvitedBy)>> RejectPendingMentorInvitesForTopicAsync(Guid topicId, Guid exceptInvitationId, CancellationToken ct);
}
