using Teammy.Application.Announcements.Dtos;

namespace Teammy.Application.Common.Interfaces;

public interface IAnnouncementNotifier
{
    Task NotifyCreatedAsync(AnnouncementDto announcement, IReadOnlyList<AnnouncementRecipient> recipients, CancellationToken ct);
}
