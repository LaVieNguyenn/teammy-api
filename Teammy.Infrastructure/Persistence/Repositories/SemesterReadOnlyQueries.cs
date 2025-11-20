using Microsoft.EntityFrameworkCore;
using Teammy.Application.Common.Interfaces;
using Teammy.Application.Semesters.Dtos;
using Teammy.Domain.Semesters;
using Teammy.Infrastructure.Persistence;
using Teammy.Infrastructure.Persistence.Models;

namespace Teammy.Infrastructure.Persistence.Repositories;

public sealed class SemesterReadOnlyQueries : ISemesterReadOnlyQueries
{
    private readonly AppDbContext _db;

    public SemesterReadOnlyQueries(AppDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<SemesterSummaryDto>> ListAsync(CancellationToken ct)
    {
        var now = DateTime.UtcNow;

        var items = await _db.semesters
            .Include(s => s.semester_policy)
            .OrderByDescending(s => s.year)
            .ThenBy(s => s.season)
            .ToListAsync(ct);

        return items.Select(s => new SemesterSummaryDto(
            s.semester_id,
            s.season ?? string.Empty,
            s.year ?? 0,
            s.start_date ?? default,  
            s.end_date ?? default,
            s.is_active,
            GetPhase(now, s)
        )).ToList();
    }

    public async Task<SemesterDetailDto?> GetByIdAsync(Guid semesterId, CancellationToken ct)
    {
        var now = DateTime.UtcNow;

        var s = await _db.semesters
            .Include(x => x.semester_policy)
            .SingleOrDefaultAsync(x => x.semester_id == semesterId, ct);

        return s is null ? null : MapDetail(now, s);
    }

    public async Task<SemesterDetailDto?> GetActiveAsync(CancellationToken ct)
    {
        var now = DateTime.UtcNow;

        var s = await _db.semesters
            .Include(x => x.semester_policy)
            .SingleOrDefaultAsync(x => x.is_active, ct);

        return s is null ? null : MapDetail(now, s);
    }
    private static SemesterDetailDto MapDetail(DateTime now, semester s)
    {
        SemesterPolicyDto? p = null;
        if (s.semester_policy is not null)
        {
            var sp = s.semester_policy;
            p = new SemesterPolicyDto(
                sp.team_self_select_start,
                sp.team_self_select_end,
                sp.team_suggest_start,
                sp.topic_self_select_start,
                sp.topic_self_select_end,
                sp.topic_suggest_start,
                sp.desired_group_size_min,
                sp.desired_group_size_max
            );
        }

        return new SemesterDetailDto(
            s.semester_id,
            s.season ?? string.Empty,
            s.year ?? 0,
            s.start_date ?? default,
            s.end_date ?? default,
            s.is_active,
            GetPhase(now, s),
            p
        );
    }

    private static SemesterPhase GetPhase(DateTime nowUtc, semester s)
    {
        var p = s.semester_policy;
        if (p is null) return SemesterPhase.Unknown;
        var today = DateOnly.FromDateTime(nowUtc);
        var teamSelfStart = ToDateOnly(p.team_self_select_start);
        var teamSelfEnd = ToDateOnly(p.team_self_select_end);
        var topicSelfStart = ToDateOnly(p.topic_self_select_start);
        var topicSelfEnd = ToDateOnly(p.topic_self_select_end);
        var semesterEnd = s.end_date ?? topicSelfEnd;
        if (teamSelfStart == default ||
            teamSelfEnd == default ||
            topicSelfStart == default ||
            topicSelfEnd == default)
        {
            return SemesterPhase.Unknown;
        }

        if (today < teamSelfStart) return SemesterPhase.BeforeTeamSelection;
        if (today <= teamSelfEnd) return SemesterPhase.TeamSelfSelection;
        if (today < topicSelfStart) return SemesterPhase.TeamSuggesting;
        if (today <= topicSelfEnd) return SemesterPhase.TopicSelfSelection;
        if (today <= semesterEnd) return SemesterPhase.TopicSuggesting;
        return SemesterPhase.Finished;
    }
    private static DateOnly ToDateOnly(DateOnly d) => d;

    private static DateOnly ToDateOnly(DateOnly? d) =>
        d ?? default;

    private static DateOnly ToDateOnly(DateTime dt) =>
        DateOnly.FromDateTime(dt);

    private static DateOnly ToDateOnly(DateTime? dt) =>
        dt.HasValue ? DateOnly.FromDateTime(dt.Value) : default;

}
