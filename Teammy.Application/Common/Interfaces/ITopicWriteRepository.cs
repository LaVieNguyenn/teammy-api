using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Teammy.Application.Topics.Dtos;

namespace Teammy.Application.Common.Interfaces
{
    public interface ITopicWriteRepository
    {
        Task<Guid> CreateAsync(CreateTopicRequest req, Guid createdBy, CancellationToken ct);
        Task UpdateAsync(Guid topicId, UpdateTopicRequest req, CancellationToken ct);
        Task DeleteAsync(Guid topicId, CancellationToken ct);
        Task SetStatusAsync(Guid topicId, string status, CancellationToken ct);

        Task<(Guid topicId, bool created)> UpsertAsync(
            Guid semesterId,
            string title,
            string? description,
            string status,
            Guid? majorId,
            string? source,
            string? sourceFileName,
            string? sourceFileType,
            long? sourceFileSize,
            IReadOnlyList<string>? skills,
            Guid createdBy,
            CancellationToken ct);
    }
}
