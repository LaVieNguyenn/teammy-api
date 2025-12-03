using System.Linq;
using Microsoft.AspNetCore.SignalR;
using Teammy.Application.Announcements.Dtos;
using Teammy.Application.Common.Interfaces;

namespace Teammy.Api.Hubs;

public sealed class AnnouncementNotifier(IHubContext<NotificationHub> hubContext) : IAnnouncementNotifier
{
    private readonly IHubContext<NotificationHub> _hubContext = hubContext;

    public Task NotifyCreatedAsync(AnnouncementDto announcement, IReadOnlyList<AnnouncementRecipient> recipients, CancellationToken ct)
    {
        if (recipients.Count == 0)
            return Task.CompletedTask;

        var tasks = recipients
            .Select(recipient => _hubContext.Clients.User(recipient.UserId.ToString())
                .SendAsync("AnnouncementCreated", announcement, cancellationToken: ct))
            .ToArray();

        return Task.WhenAll(tasks);
    }
}
