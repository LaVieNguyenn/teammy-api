using Teammy.Application.Groups.Dtos;

namespace Teammy.Application.Common.Interfaces;

public interface IGroupReadOnlyQueries
{
    Task<Guid?> GetActiveSemesterIdAsync(CancellationToken ct);

    Task<IReadOnlyList<GroupSummaryDto>> ListGroupsAsync(
        string? status, Guid? majorId, Guid? topicId, CancellationToken ct);

    Task<GroupDetailDto?> GetGroupAsync(Guid id, CancellationToken ct);

    Task<IReadOnlyList<JoinRequestDto>> GetPendingJoinRequestsAsync(Guid groupId, CancellationToken ct);

    Task<bool> IsLeaderAsync(Guid groupId, Guid userId, CancellationToken ct);

    Task<bool> HasActiveMembershipInSemesterAsync(Guid userId, Guid semesterId, CancellationToken ct);

    Task<(int MaxMembers, int ActiveCount)> GetGroupCapacityAsync(Guid groupId, CancellationToken ct);

    Task<Guid?> GetLeaderGroupIdAsync(Guid userId, Guid semesterId, CancellationToken ct);
}
