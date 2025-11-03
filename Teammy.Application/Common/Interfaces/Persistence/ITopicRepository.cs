using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Teammy.Application.Common.Pagination;
using Teammy.Application.Topics.ReadModels;

namespace Teammy.Application.Common.Interfaces.Persistence
{
    public interface ITopicRepository
    {
        Task<TopicReadModel?> GetByIdAsync(Guid id, CancellationToken ct);
        Task<bool> ExistsTitleInTermAsync(Guid termId, string title, Guid excludeId, CancellationToken ct);
        Task<bool> UpdateAsync(Guid id, string? title, string? code, string? description, Guid? departmentId, Guid? majorId, CancellationToken ct);
        Task<bool> ArchiveAsync(Guid id, CancellationToken ct);
        Task<PagedResult<TopicReadModel>> SearchAsync(Guid termId, string? status, Guid? departmentId, Guid? majorId, string? q, string? sort, int page, int size, CancellationToken ct);

    }
}
