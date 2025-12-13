using Teammy.Application.Announcements.Dtos;
using Teammy.Application.Common.Dtos;

namespace Teammy.Application.Common.Interfaces;

public interface IAnnouncementRecipientQueries
{
    Task<IReadOnlyList<AnnouncementRecipient>> ResolveRecipientsAsync(
        string scope,
        Guid? semesterId,
        string? targetRole,
        Guid? targetGroupId,
        CancellationToken ct);

    Task<PaginatedResult<AnnouncementRecipient>> ListRecipientsAsync(
        string scope,
        Guid? semesterId,
        string? targetRole,
        Guid? targetGroupId,
        int page,
        int pageSize,
        CancellationToken ct);
}
