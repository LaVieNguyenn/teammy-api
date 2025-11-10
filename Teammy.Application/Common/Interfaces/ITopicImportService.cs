using Teammy.Application.Topics.Dtos;

namespace Teammy.Application.Common.Interfaces;

public interface ITopicImportService
{
    Task<byte[]> BuildTemplateAsync(CancellationToken ct);         
    Task<TopicImportResult> ImportAsync(Stream excelStream, Guid performedBy, CancellationToken ct);
}
