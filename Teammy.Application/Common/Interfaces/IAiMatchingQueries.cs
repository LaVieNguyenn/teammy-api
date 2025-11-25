using Teammy.Application.Ai.Models;

namespace Teammy.Application.Common.Interfaces;

public interface IAiMatchingQueries
{
    Task<StudentProfileSnapshot?> GetStudentProfileAsync(Guid userId, Guid semesterId, CancellationToken ct);
    Task<IReadOnlyList<StudentProfileSnapshot>> ListUnassignedStudentsAsync(Guid semesterId, Guid? majorId, CancellationToken ct);
    Task<IReadOnlyList<GroupCapacitySnapshot>> ListGroupCapacitiesAsync(Guid semesterId, Guid? majorId, CancellationToken ct);
    Task<IReadOnlyList<TopicMatchSnapshot>> ListTopicMatchesAsync(Guid groupId, CancellationToken ct);
    Task<IReadOnlyList<TopicAvailabilitySnapshot>> ListTopicAvailabilityAsync(Guid semesterId, Guid? majorId, CancellationToken ct);
    Task<IReadOnlyDictionary<Guid, GroupRoleMixSnapshot>> GetGroupRoleMixAsync(IEnumerable<Guid> groupIds, CancellationToken ct);
    Task RefreshStudentsPoolAsync(CancellationToken ct);
}
