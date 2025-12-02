using Teammy.Application.Announcements.Dtos;

namespace Teammy.Application.Common.Interfaces;

public interface IAnnouncementRecipientQueries
{
    Task<IReadOnlyList<AnnouncementRecipient>> ResolveRecipientsAsync(
        string scope,
        Guid? semesterId,
        string? targetRole,
        Guid? targetGroupId,
        CancellationToken ct);
}
