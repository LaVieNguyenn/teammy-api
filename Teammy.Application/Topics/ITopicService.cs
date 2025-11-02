using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Teammy.Application.Common.Results;
using Teammy.Application.Topics.ReadModels;

namespace Teammy.Application.Topics
{
    public interface ITopicService
    {
        Task<TopicReadModel?> GetByIdAsync(Guid id, CancellationToken ct);
        Task<OperationResult> UpdateAsync(Guid id, string? title, string? code, string? description, Guid? departmentId, Guid? majorId, CancellationToken ct);
        Task<OperationResult> ArchiveAsync(Guid id, CancellationToken ct);
    }
}
