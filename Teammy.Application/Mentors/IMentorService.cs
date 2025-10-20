using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Teammy.Application.Common.Pagination;
using Teammy.Application.Common.Results;
using Teammy.Application.Mentors.ReadModels;

namespace Teammy.Application.Mentors
{
    public interface IMentorService
    {
        Task<PagedResult<OpenGroupReadModel>> ListOpenGroupsAsync(
            Guid termId, Guid? departmentId, string? topic, int page, int size, CancellationToken ct);

        Task<OperationResult> SelfAssignAsync(Guid groupId, Guid mentorId, CancellationToken ct);

        Task<bool> UnassignAsync(Guid groupId, Guid mentorId, CancellationToken ct);

        Task<IReadOnlyList<AssignedGroupReadModel>> GetAssignedGroupsAsync(Guid mentorId, CancellationToken ct);

        Task<MentorProfileReadModel?> GetMyProfileAsync(Guid mentorId, CancellationToken ct);

        Task<OperationResult> UpdateMyProfileAsync(Guid mentorId, string? bio, IEnumerable<string>? skills, IEnumerable<object>? availability, CancellationToken ct);
    }
}
