using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Teammy.Application.Common.Pagination;
using Teammy.Application.Mentors.ReadModels;

namespace Teammy.Application.Common.Interfaces.Persistence
{
    public interface IMentorRepository
    {
        Task<PagedResult<OpenGroupReadModel>> ListOpenGroupsAsync(
            Guid termId, Guid? departmentId, string? topic, int page, int size, CancellationToken ct);

        Task<int> CountAssignedTopicsInTermAsync(Guid mentorId, Guid termId, CancellationToken ct);

        Task<bool> ExistsMentorOnTopicAsync(Guid topicId, Guid mentorId, CancellationToken ct);

        Task<bool> AddMentorToTopicAsync(Guid topicId, Guid mentorId, string roleOnTopic, CancellationToken ct);

        Task<(Guid? TopicId, Guid? TermId)> GetGroupTopicAndTermAsync(Guid groupId, CancellationToken ct);

        Task<bool> RemoveMentorFromGroupAsync(Guid groupId, Guid mentorId, CancellationToken ct);

        Task<IReadOnlyList<AssignedGroupReadModel>> GetAssignedGroupsAsync(Guid mentorId, CancellationToken ct);

        Task<MentorProfileReadModel?> GetMentorProfileAsync(Guid mentorId, CancellationToken ct);
    }

}
