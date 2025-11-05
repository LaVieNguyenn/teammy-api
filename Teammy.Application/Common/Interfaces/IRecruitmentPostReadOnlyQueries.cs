using Teammy.Application.Posts.Dtos;

namespace Teammy.Application.Common.Interfaces;

public interface IRecruitmentPostReadOnlyQueries
{
    Task<Guid?> GetActiveSemesterIdAsync(CancellationToken ct);

    Task<RecruitmentPostDetailDto?> GetAsync(Guid id, CancellationToken ct);

    Task<IReadOnlyList<RecruitmentPostSummaryDto>> ListAsync(string? skills, Guid? majorId, string? status, CancellationToken ct);

    Task<IReadOnlyList<ApplicationDto>> ListApplicationsAsync(Guid postId, CancellationToken ct);

    Task<(Guid? GroupId, Guid SemesterId, Guid? OwnerUserId)> GetPostOwnerAsync(Guid postId, CancellationToken ct);
}

