using Teammy.Application.Activity.Dtos;

namespace Teammy.Application.Common.Interfaces;

public interface IActivityLogRepository
{
    Task<ActivityLogDto> InsertAsync(ActivityLogRecord record, CancellationToken ct);
    Task<IReadOnlyList<ActivityLogDto>> ListAsync(ActivityLogListRequest request, CancellationToken ct);
}
