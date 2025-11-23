using Teammy.Application.Groups.Dtos;

namespace Teammy.Application.Common.Interfaces;

public interface IGroupReadOnlyQueries
{
    Task<Guid?> GetActiveSemesterIdAsync(CancellationToken ct);

    Task<IReadOnlyList<GroupSummaryDto>> ListGroupsAsync(
        string? status, Guid? majorId, Guid? topicId, CancellationToken ct);

    Task<GroupDetailDto?> GetGroupAsync(Guid id, CancellationToken ct);

    Task<bool> IsLeaderAsync(Guid groupId, Guid userId, CancellationToken ct);

    Task<bool> HasActiveMembershipInSemesterAsync(Guid userId, Guid semesterId, CancellationToken ct);
    Task<bool> HasActiveGroupAsync(Guid userId, Guid semesterId, CancellationToken ct);

    Task<(int MaxMembers, int ActiveCount)> GetGroupCapacityAsync(Guid groupId, CancellationToken ct);

    Task<Guid?> GetLeaderGroupIdAsync(Guid userId, Guid semesterId, CancellationToken ct);

    Task<IReadOnlyList<Teammy.Application.Groups.Dtos.MyGroupDto>> ListMyGroupsAsync(Guid userId, Guid? semesterId, CancellationToken ct);

    Task<IReadOnlyList<Teammy.Application.Groups.Dtos.GroupMemberDto>> ListActiveMembersAsync(Guid groupId, CancellationToken ct);
    Task<bool> IsActiveMemberAsync(Guid groupId, Guid userId, CancellationToken ct);

    Task<GroupMentorDto?> GetMentorAsync(Guid groupId, CancellationToken ct);

    Task<Teammy.Application.Groups.Dtos.UserGroupCheckDto> CheckUserGroupAsync(Guid userId, Guid? semesterId, bool includePending, CancellationToken ct);

    Task<(Guid SemesterId, string? Season, int? Year, DateOnly? StartDate, DateOnly? EndDate, bool IsActive)?> GetSemesterAsync(Guid semesterId, CancellationToken ct);
    Task<(Guid MajorId, string MajorName)?> GetMajorAsync(Guid majorId, CancellationToken ct);
    Task<(Guid TopicId, string Title, string? Description, string Status, Guid CreatedBy, DateTime? CreatedAt)?> GetTopicAsync(Guid topicId, CancellationToken ct);

    Task<bool> GroupNameExistsAsync(Guid semesterId, string name, Guid? excludeGroupId, CancellationToken ct);

    Task<IReadOnlyList<Teammy.Application.Groups.Dtos.GroupPendingItemDto>> GetUnifiedPendingAsync(Guid groupId, CancellationToken ct);
}
