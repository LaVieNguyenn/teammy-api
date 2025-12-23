using System;
using System.Collections.Generic;
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
        IReadOnlyList<Guid>? targetGroupIds,
        IReadOnlyList<Guid>? targetUserIds,
        CancellationToken ct);

    Task<PaginatedResult<AnnouncementRecipient>> ListRecipientsAsync(
        string scope,
        Guid? semesterId,
        string? targetRole,
        Guid? targetGroupId,
        IReadOnlyList<Guid>? targetGroupIds,
        IReadOnlyList<Guid>? targetUserIds,
        int page,
        int pageSize,
        CancellationToken ct);

    Task<IReadOnlyList<Guid>> ResolveTargetGroupIdsAsync(
        string scope,
        Guid semesterId,
        IReadOnlyList<Guid>? targetGroupIds,
        CancellationToken ct);

    Task<IReadOnlyList<Guid>> ResolveTargetUserIdsAsync(
        string scope,
        Guid semesterId,
        IReadOnlyList<Guid>? targetUserIds,
        CancellationToken ct);
}
