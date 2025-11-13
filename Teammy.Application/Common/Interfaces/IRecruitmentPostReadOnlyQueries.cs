using Teammy.Application.Posts.Dtos;

namespace Teammy.Application.Common.Interfaces;

public interface IRecruitmentPostReadOnlyQueries
{
    Task<Guid?> GetActiveSemesterIdAsync(CancellationToken ct);

    Task<RecruitmentPostDetailDto?> GetAsync(Guid id, ExpandOptions expand, Guid? currentUserId, CancellationToken ct);

    Task<IReadOnlyList<RecruitmentPostSummaryDto>> ListAsync(string? skills, Guid? majorId, string? status, ExpandOptions expand, Guid? currentUserId, CancellationToken ct);

    // Profile posts (student looking for group)
    Task<IReadOnlyList<ProfilePostSummaryDto>> ListProfilePostsAsync(string? skills, Guid? majorId, string? status, ExpandOptions expand, CancellationToken ct);

    Task<ProfilePostDetailDto?> GetProfilePostAsync(Guid id, ExpandOptions expand, CancellationToken ct);

    Task<IReadOnlyList<ApplicationDto>> ListApplicationsAsync(Guid postId, CancellationToken ct);

    Task<(Guid? GroupId, Guid SemesterId, Guid? OwnerUserId)> GetPostOwnerAsync(Guid postId, CancellationToken ct);

    // Posts the given user has applied to (via candidates)
    Task<IReadOnlyList<RecruitmentPostSummaryDto>> ListAppliedByUserAsync(Guid userId, ExpandOptions expand, CancellationToken ct);

    // Find pending application by user to any post of a specific group
    Task<(Guid ApplicationId, Guid PostId)?> FindPendingApplicationInGroupAsync(Guid groupId, Guid userId, CancellationToken ct);

    // Find any application by user for a specific post
    Task<(Guid ApplicationId, string Status)?> FindApplicationByPostAndUserAsync(Guid postId, Guid userId, CancellationToken ct);
}
