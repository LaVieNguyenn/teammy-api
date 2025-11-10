using Teammy.Application.Common.Interfaces;
using Teammy.Application.Topics.Dtos;

namespace Teammy.Application.Topics.Services;

public sealed class TopicService(
    ITopicReadOnlyQueries read,
    ITopicWriteRepository write,
    ITopicImportService excel
)
{
    public Task<IReadOnlyList<TopicListItemDto>> GetAllAsync(string? q, Guid? semesterId, string? status, Guid? majorId, CancellationToken ct)
        => read.GetAllAsync(q, semesterId, status, majorId, ct);

    public Task<TopicDetailDto?> GetByIdAsync(Guid id, CancellationToken ct)
        => read.GetByIdAsync(id, ct);

    public Task<Guid> CreateAsync(Guid currentUserId, CreateTopicRequest req, CancellationToken ct)
        => write.CreateAsync(NormalizeCreate(req), currentUserId, ct);

    public Task UpdateAsync(Guid id, UpdateTopicRequest req, CancellationToken ct)
        => write.UpdateAsync(id, NormalizeUpdate(req), ct);

    public Task DeleteAsync(Guid id, CancellationToken ct)
        => write.DeleteAsync(id, ct);

    public Task<byte[]> BuildTemplateAsync(CancellationToken ct)
        => excel.BuildTemplateAsync(ct);

    public Task<TopicImportResult> ImportAsync(Guid currentUserId, Stream s, CancellationToken ct)
        => excel.ImportAsync(s, currentUserId, ct);

    private static CreateTopicRequest NormalizeCreate(CreateTopicRequest r)
        => r with { Title = r.Title.Trim(), Status = string.IsNullOrWhiteSpace(r.Status) ? "open" : r.Status!.Trim().ToLowerInvariant() };

    private static UpdateTopicRequest NormalizeUpdate(UpdateTopicRequest r)
        => r with { Title = r.Title.Trim(), Status = r.Status.Trim().ToLowerInvariant() };
}
