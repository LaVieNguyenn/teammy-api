using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Teammy.Application.Common.Interfaces.Topics;

public sealed record TopicImportResult(Guid JobId, int TotalRows, int SuccessRows, int ErrorRows);

public interface ITopicImportService
{
    Task<TopicImportResult> ImportAsync(
        Guid termId,
        Stream fileStream,
        string fileName,
        Guid actorId,
        CancellationToken ct,
        Guid? majorId = null);
}
