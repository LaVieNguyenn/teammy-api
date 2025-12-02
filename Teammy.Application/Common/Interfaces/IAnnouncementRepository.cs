using Teammy.Application.Announcements.Dtos;

namespace Teammy.Application.Common.Interfaces;

public interface IAnnouncementRepository
{
    Task<AnnouncementDto> CreateAsync(CreateAnnouncementCommand command, CancellationToken ct);
}
