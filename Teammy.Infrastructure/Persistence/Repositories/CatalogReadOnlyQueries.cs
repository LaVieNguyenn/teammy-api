using Microsoft.EntityFrameworkCore;
using Teammy.Application.Catalog.Dtos;
using Teammy.Application.Common.Interfaces;
using Teammy.Infrastructure.Persistence;

namespace Teammy.Infrastructure.Persistence.Repositories;

public sealed class CatalogReadOnlyQueries(AppDbContext db) : ICatalogReadOnlyQueries
{
    public Task<SemesterDto?> GetActiveSemesterAsync(CancellationToken ct)
        => db.semesters.AsNoTracking()
            .Where(s => s.is_active)
            .Select(s => new SemesterDto(s.semester_id, s.season, s.year, s.start_date, s.end_date, s.is_active))
            .FirstOrDefaultAsync(ct);

    public Task<IReadOnlyList<SemesterDto>> ListSemestersAsync(CancellationToken ct)
        => db.semesters.AsNoTracking()
            .OrderByDescending(s => s.start_date)
            .ThenByDescending(s => s.year)
            .Select(s => new SemesterDto(s.semester_id, s.season, s.year, s.start_date, s.end_date, s.is_active))
            .ToListAsync(ct)
            .ContinueWith(t => (IReadOnlyList<SemesterDto>)t.Result, ct);

    public Task<IReadOnlyList<MajorDto>> ListMajorsAsync(CancellationToken ct)
        => db.majors.AsNoTracking()
            .OrderBy(m => m.major_name)
            .Select(m => new MajorDto(m.major_id, m.major_name))
            .ToListAsync(ct)
            .ContinueWith(t => (IReadOnlyList<MajorDto>)t.Result, ct);

    public Task<IReadOnlyList<TopicDto>> ListTopicsAsync(CancellationToken ct)
        => db.topics.AsNoTracking()
            .OrderByDescending(t => t.created_at)
            .Select(t => new TopicDto(t.topic_id, t.semester_id, t.major_id, t.title, t.description, t.status, t.created_by, t.created_at))
            .ToListAsync(ct)
            .ContinueWith(t => (IReadOnlyList<TopicDto>)t.Result, ct);
}

