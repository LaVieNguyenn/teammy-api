using Microsoft.AspNetCore.SignalR;
using Teammy.Application.Activity.Dtos;
using Teammy.Application.Common.Interfaces;

namespace Teammy.Api.Hubs;

public sealed class ActivityLogNotifier(IHubContext<NotificationHub> hubContext) : IActivityLogNotifier
{
    private readonly IHubContext<NotificationHub> _hubContext = hubContext;
    public const string AdminGroupName = "admins";

    public Task NotifyAsync(ActivityLogDto dto, CancellationToken ct)
        => _hubContext.Clients.Group(AdminGroupName)
            .SendAsync("ActivityLogCreated", dto, cancellationToken: ct);
}
