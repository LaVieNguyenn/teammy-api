using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Teammy.Application.Common.Interfaces.Persistence;
using Teammy.Application.Common.Pagination;
using Teammy.Application.Common.Results;
using Teammy.Application.Topics.ReadModels;

namespace Teammy.Application.Topics
{
    public sealed class TopicService : ITopicService
    {
        private readonly ITopicRepository _repo;
        public TopicService(ITopicRepository repo) => _repo = repo;

        public Task<TopicReadModel?> GetByIdAsync(Guid id, CancellationToken ct)
            => _repo.GetByIdAsync(id, ct);

        public async Task<OperationResult> UpdateAsync(Guid id, string? title, string? code, string? description, Guid? departmentId, Guid? majorId, CancellationToken ct)
        {
            var current = await _repo.GetByIdAsync(id, ct);
            if (current is null) return OperationResult.Fail("TOPIC_NOT_FOUND", 404);

            if (!string.IsNullOrWhiteSpace(title))
            {
                if (await _repo.ExistsTitleInTermAsync(current.TermId, title, id, ct))
                    return OperationResult.Fail("DUPLICATE_TITLE", 409);
                title = title.Trim();
            }

            var ok = await _repo.UpdateAsync(id,
                title,
                code is null ? null : code.Trim(),
                description is null ? null : description.Trim(),
                departmentId,
                majorId,
                ct);
            return ok ? OperationResult.Success() : OperationResult.Fail("TOPIC_NOT_FOUND", 404);
        }

        public async Task<OperationResult> ArchiveAsync(Guid id, CancellationToken ct)
        {
            var ok = await _repo.ArchiveAsync(id, ct);
            return ok ? OperationResult.Success() : OperationResult.Fail("TOPIC_NOT_FOUND", 404);
        }
        public Task<PagedResult<TopicReadModel>> SearchAsync(Guid termId, string? status, Guid? departmentId, Guid? majorId, string? q, string? sort, int page, int size, CancellationToken ct)
       => _repo.SearchAsync(termId, status, departmentId, majorId, q, sort, page, size, ct);

    }
}
