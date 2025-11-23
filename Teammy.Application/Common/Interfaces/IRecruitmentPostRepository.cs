using Teammy.Application.Posts.Dtos;

namespace Teammy.Application.Common.Interfaces;

public interface IRecruitmentPostRepository
{
    Task<Guid> CreateRecruitmentPostAsync(
        Guid semesterId,
        string postType,
        Guid? groupId,
        Guid? userId,
        Guid? majorId,
        string title,
        string? description,
        string? skills,
        DateTime? applicationDeadline,
        CancellationToken ct);

    Task<Guid> CreateApplicationAsync(Guid postId, Guid? applicantUserId, Guid? applicantGroupId, Guid appliedByUserId, string? message, CancellationToken ct);

    Task UpdatePostAsync(Guid postId, string? title, string? description, string? skills, string? status, CancellationToken ct);
    Task DeletePostAsync(Guid postId, CancellationToken ct);

    Task UpdateApplicationStatusAsync(Guid applicationId, string newStatus, CancellationToken ct);
    Task ExpireOpenPostsAsync(DateTime utcNow, CancellationToken ct);

    Task<int> CloseAllOpenPostsForGroupAsync(Guid groupId, CancellationToken ct);

    Task<int> CloseAllOpenPostsExceptAsync(Guid groupId, Guid keepPostId, CancellationToken ct);

    Task<int> SetOpenPostsStatusForGroupAsync(Guid groupId, string newStatus, CancellationToken ct);

    Task<int> RejectPendingApplicationsForUserInGroupAsync(Guid groupId, Guid userId, CancellationToken ct);

    Task ReactivateApplicationAsync(Guid applicationId, string? message, CancellationToken ct);

    Task<int> RejectPendingProfileInvitationsAsync(Guid ownerUserId, Guid semesterId, Guid keepCandidateId, CancellationToken ct);
}
