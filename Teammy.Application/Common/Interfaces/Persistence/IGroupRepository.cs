using Teammy.Application.Common.Pagination;
using Teammy.Application.Groups.ReadModels;

namespace Teammy.Application.Common.Interfaces.Persistence;

public interface IGroupRepository
{
    Task<bool> UserHasActiveGroupInTermAsync(Guid userId, Guid termId, CancellationToken ct);
    Task<Guid> CreateGroupAsync(Guid termId, string name, int capacity, Guid? topicId, string? description, string? techStack, string? githubUrl, Guid creatorUserId, CancellationToken ct);
    Task<GroupReadModel?> GetByIdAsync(Guid groupId, CancellationToken ct);
    Task<PagedResult<GroupReadModel>> ListOpenAsync(Guid termId, Guid? topicId, Guid? departmentId, Guid? majorId, string? q, int page, int size, CancellationToken ct);
    Task<bool> AddJoinRequestAsync(Guid groupId, Guid userId, Guid termId, CancellationToken ct);
    Task<(bool Ok, string? Reason)> LeaveAsync(Guid groupId, Guid userId, CancellationToken ct);
}

