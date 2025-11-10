using Teammy.Application.Topics.Dtos;

namespace Teammy.Application.Common.Interfaces;

public interface ITopicReadOnlyQueries
{
    Task<IReadOnlyList<TopicListItemDto>> GetAllAsync(string? q, Guid? semesterId, string? status, Guid? majorId, CancellationToken ct);
    Task<TopicDetailDto?> GetByIdAsync(Guid topicId, CancellationToken ct);
    Task<Guid?> FindSemesterIdByCodeAsync(string semesterCode, CancellationToken ct);
    Task<Guid?> FindMajorIdByNameAsync(string majorName, CancellationToken ct);
}
