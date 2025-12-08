using System.Text.Json;
using Teammy.Application.Activity.Dtos;
using Teammy.Application.Common.Interfaces;

namespace Teammy.Application.Activity.Services;

public sealed class ActivityLogService(
    IActivityLogRepository repository,
    IActivityLogNotifier notifier)
{
    private readonly IActivityLogRepository _repository = repository;
    private readonly IActivityLogNotifier _notifier = notifier;
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public async Task<ActivityLogDto> LogAsync(ActivityLogCreateRequest request, CancellationToken ct)
    {
        var metadataJson = request.Metadata is null
            ? null
            : JsonSerializer.Serialize(request.Metadata, SerializerOptions);

        var record = new ActivityLogRecord(
            request.ActorId,
            request.EntityType,
            request.Action,
            request.GroupId,
            request.EntityId,
            request.TargetUserId,
            request.Message,
            metadataJson,
            string.IsNullOrWhiteSpace(request.Status) ? "success" : request.Status,
            request.Platform,
            string.IsNullOrWhiteSpace(request.Severity) ? "info" : request.Severity);

        var dto = await _repository.InsertAsync(record, ct);
        await _notifier.NotifyAsync(dto, ct);
        return dto;
    }

    public Task<IReadOnlyList<ActivityLogDto>> GetRecentAsync(ActivityLogListRequest request, CancellationToken ct)
        => _repository.ListAsync(request, ct);
}
