using System.Collections.Generic;
using Teammy.Application.Ai.Models;

namespace Teammy.Application.Common.Interfaces;

public interface IAiMatchingQueries
{
    Task<StudentProfileSnapshot?> GetStudentProfileAsync(Guid userId, Guid semesterId, CancellationToken ct);
    Task<IReadOnlyList<StudentProfileSnapshot>> ListUnassignedStudentsAsync(Guid semesterId, Guid? majorId, CancellationToken ct);
    Task<IReadOnlyList<GroupCapacitySnapshot>> ListGroupCapacitiesAsync(Guid semesterId, Guid? majorId, CancellationToken ct);
    Task<IReadOnlyDictionary<Guid, GroupRoleMixSnapshot>> GetGroupRoleMixAsync(IEnumerable<Guid> groupIds, CancellationToken ct);
    Task<IReadOnlyList<RecruitmentPostSnapshot>> ListOpenRecruitmentPostsAsync(Guid semesterId, Guid? majorId, CancellationToken ct);
    Task<IReadOnlyList<ProfilePostSnapshot>> ListOpenProfilePostsAsync(Guid semesterId, Guid? majorId, CancellationToken ct);
    Task<IReadOnlyList<GroupMemberSkillSnapshot>> ListGroupMemberSkillsAsync(Guid groupId, CancellationToken ct);
    Task<IReadOnlyList<string>> ListGroupMemberDesiredPositionsAsync(Guid groupId, CancellationToken ct);
    Task<IReadOnlyList<TopicAvailabilitySnapshot>> ListTopicAvailabilityAsync(Guid semesterId, Guid? majorId, CancellationToken ct);
    Task<int> CountGroupsWithoutTopicAsync(Guid semesterId, CancellationToken ct);
    Task<int> CountGroupsUnderCapacityAsync(Guid semesterId, CancellationToken ct);
    Task<int> CountUnassignedStudentsAsync(Guid semesterId, CancellationToken ct);
    Task<IReadOnlyList<GroupOverviewSnapshot>> ListGroupsWithoutTopicAsync(Guid semesterId, CancellationToken ct);
    Task<IReadOnlyList<GroupOverviewSnapshot>> ListGroupsUnderCapacityAsync(Guid semesterId, CancellationToken ct);
    Task RefreshStudentsPoolAsync(CancellationToken ct);
}
