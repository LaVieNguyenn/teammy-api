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

        return items.Select(s =>
        {
            SemesterPolicyDto? policy = null;
            if (s.semester_policy is not null)
            {
                var p = s.semester_policy;
                policy = new SemesterPolicyDto(
                    p.team_self_select_start,
                    p.team_self_select_end,
                    p.team_suggest_start,
                    p.topic_self_select_start,
                    p.topic_self_select_end,
                    p.topic_suggest_start,
                    p.desired_group_size_min,
                    p.desired_group_size_max
                );
            }

            return new SemesterSummaryDto(
                s.semester_id,
                s.season ?? string.Empty,
                s.year ?? 0,
                s.start_date ?? default,
                s.end_date ?? default,
                s.is_active,
                GetPhase(now, s),
                policy
            );
        }).ToList();
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

    public Task<SemesterPolicyDto?> GetPolicyAsync(Guid semesterId, CancellationToken ct)
        => _db.semester_policies.AsNoTracking()
            .Where(p => p.semester_id == semesterId)
            .Select(p => new SemesterPolicyDto(
                p.team_self_select_start,
                p.team_self_select_end,
                p.team_suggest_start,
                p.topic_self_select_start,
                p.topic_self_select_end,
                p.topic_suggest_start,
                p.desired_group_size_min,
                p.desired_group_size_max))
            .FirstOrDefaultAsync(ct);

    public async Task<bool> ExistsAsync(string normalizedSeason, int year, Guid? excludeSemesterId, CancellationToken ct)
    {
        var season = NormalizeSeason(normalizedSeason);
        var items = await _db.semesters.AsNoTracking()
            .Select(s => new
            {
                s.semester_id,
                s.season,
                s.year,
                s.start_date,
                s.end_date
            })
            .ToListAsync(ct);

        return items.Any(s =>
            string.Equals(NormalizeSeason(s.season), season, StringComparison.Ordinal) &&
            ResolveYear(s.year, s.start_date, s.end_date) == year &&
            (!excludeSemesterId.HasValue || s.semester_id != excludeSemesterId.Value));
    }

    public async Task<int> CountByYearAsync(int year, CancellationToken ct)
    {
        var items = await _db.semesters.AsNoTracking()
            .Select(s => new { s.year, s.start_date, s.end_date })
            .ToListAsync(ct);

        return items.Count(s => ResolveYear(s.year, s.start_date, s.end_date) == year);
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

    private static string NormalizeSeason(string? season)
        => string.IsNullOrWhiteSpace(season) ? string.Empty : season.Trim().ToUpperInvariant();

    private static int ResolveYear(int? year, DateOnly? start, DateOnly? end)
    {
        if (year.HasValue) return year.Value;
        if (start.HasValue) return start.Value.Year;
        if (end.HasValue) return end.Value.Year;
        return 0;
    }
}
