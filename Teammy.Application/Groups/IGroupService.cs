using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Teammy.Application.Common.Pagination;
using Teammy.Application.Common.Results;
using Teammy.Application.Groups.ReadModels;

namespace Teammy.Application.Groups
{
    public interface IGroupService
    {
        Task<OperationResult> CreateAsync(Guid termId, string name, int capacity, Guid? topicId, string? description, string? techStack, string? githubUrl, Guid creatorUserId, CancellationToken ct);
        Task<GroupReadModel?> GetByIdAsync(Guid id, CancellationToken ct);
        Task<PagedResult<GroupReadModel>> ListOpenAsync(Guid termId, Guid? topicId, Guid? departmentId, Guid? majorId, string? q, int page, int size, CancellationToken ct);
        Task<OperationResult> JoinAsync(Guid groupId, Guid userId, CancellationToken ct);
        Task<OperationResult> LeaveAsync(Guid groupId, Guid userId, CancellationToken ct);
        Task<IReadOnlyList<PendingMemberReadModel>> GetPendingMembersAsync(Guid groupId, Guid leaderId, CancellationToken ct);
        Task<OperationResult> AcceptAsync(Guid groupId, Guid leaderId, Guid userId, CancellationToken ct);
        Task<OperationResult> RejectAsync(Guid groupId, Guid leaderId, Guid userId, CancellationToken ct);
    }

}
