using Teammy.Application.Announcements.Dtos;

namespace Teammy.Application.Common.Interfaces;

public interface IAnnouncementReadOnlyQueries
{
    Task<IReadOnlyList<AnnouncementDto>> ListForUserAsync(Guid userId, AnnouncementFilter filter, CancellationToken ct);
    Task<AnnouncementDto?> GetForUserAsync(Guid announcementId, Guid userId, CancellationToken ct);
}
