using Microsoft.EntityFrameworkCore;
using Teammy.Application.Common.Interfaces;
using Teammy.Application.Topics.Dtos;
using Teammy.Infrastructure.Persistence;

namespace Teammy.Infrastructure.Persistence.Repositories;

public sealed class TopicReadOnlyQueries(AppDbContext db) : ITopicReadOnlyQueries
{
    public async Task<IReadOnlyList<TopicListItemDto>> GetAllAsync(string? q, Guid? semesterId, string? status, Guid? majorId, CancellationToken ct)
    {
        var src = db.topics.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(q))
            src = src.Where(t => EF.Functions.ILike(t.title, $"%{q}%") || EF.Functions.ILike(t.description ?? "", $"%{q}%"));

        if (semesterId is not null)
            src = src.Where(t => t.semester_id == semesterId);

        if (!string.IsNullOrWhiteSpace(status))
            src = src.Where(t => t.status == status!.Trim().ToLower());

        if (majorId is not null)
            src = src.Where(t => t.major_id == majorId);

        return await src
            .OrderByDescending(t => t.created_at)
            .Select(t => new TopicListItemDto(
                t.topic_id, t.semester_id, t.major_id, t.title, t.description, t.status, t.created_by, t.created_at))
            .ToListAsync(ct);
    }

    public async Task<TopicDetailDto?> GetByIdAsync(Guid topicId, CancellationToken ct)
        => await db.topics.AsNoTracking()
            .Where(t => t.topic_id == topicId)
            .Select(t => new TopicDetailDto(
                t.topic_id, t.semester_id, t.major_id, t.title, t.description, t.status, t.created_by, t.created_at))
            .FirstOrDefaultAsync(ct);

    public async Task<Guid?> FindSemesterIdByCodeAsync(string semesterCode, CancellationToken ct)
        => await db.semesters.AsNoTracking()
            .Where(s => s.season.ToLower() == semesterCode.ToLower())
            .Select(s => (Guid?)s.semester_id)
            .FirstOrDefaultAsync(ct);

    public async Task<Guid?> FindMajorIdByNameAsync(string majorName, CancellationToken ct)
        => await db.majors.AsNoTracking()
            .Where(m => m.major_name.ToLower() == majorName.ToLower())
            .Select(m => (Guid?)m.major_id)
            .FirstOrDefaultAsync(ct);
}
