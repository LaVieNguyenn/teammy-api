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
        CancellationToken ct);

    Task<Guid> CreateApplicationAsync(Guid postId, Guid? applicantUserId, Guid? applicantGroupId, Guid appliedByUserId, string? message, CancellationToken ct);

    Task UpdatePostAsync(Guid postId, string? title, string? description, string? skills, string? status, CancellationToken ct);

    Task UpdateApplicationStatusAsync(Guid applicationId, string newStatus, CancellationToken ct);
}
