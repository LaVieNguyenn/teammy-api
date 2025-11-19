using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Teammy.Application.Topics.Dtos;

namespace Teammy.Application.Common.Interfaces
{
    public interface ITopicImportService
    {
        Task<byte[]> BuildTemplateAsync(CancellationToken ct);
        Task<TopicImportResult> ImportAsync(Stream excelStream, Guid currentUserId, CancellationToken ct);
        Task<TopicImportValidationResult> ValidateRowsAsync(
            IReadOnlyList<TopicImportPayloadRow> rows,
            CancellationToken ct);
    }
}
