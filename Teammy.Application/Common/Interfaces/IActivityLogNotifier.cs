using Teammy.Application.Activity.Dtos;

namespace Teammy.Application.Common.Interfaces;

public interface IActivityLogNotifier
{
    Task NotifyAsync(ActivityLogDto dto, CancellationToken ct);
}
