using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Teammy.Application.Common.Interfaces;
using Teammy.Application.Semesters.Dtos;
using Teammy.Infrastructure.Persistence;
using Teammy.Infrastructure.Persistence.Models;

namespace Teammy.Infrastructure.Persistence.Repositories;

public sealed class SemesterRepository : ISemesterRepository
{
    private readonly AppDbContext _db;
    private static readonly string[] SeasonSequence = ["SPRING", "SUMMER", "FALL"];

    public SemesterRepository(AppDbContext db)
    {
        _db = db;
    }
    public async Task<Guid> CreateAsync(
        string season,
        int year,
        DateOnly startDate,
        DateOnly endDate,
        CancellationToken ct)
    {
        var entity = new semester
        {
            semester_id = Guid.NewGuid(),
            season      = season,
            year        = year,
            start_date  = startDate,
            end_date    = endDate,
            is_active   = false
        };

        _db.semesters.Add(entity);
        await _db.SaveChangesAsync(ct);
        return entity.semester_id;
    }
    public async Task<bool> UpdateAsync(
        Guid semesterId,
        string season,
        int year,
        DateOnly startDate,
        DateOnly endDate,
        CancellationToken ct)
    {
        var entity = await _db.semesters
            .SingleOrDefaultAsync(s => s.semester_id == semesterId, ct);

        if (entity is null) return false;

        entity.season     = season;
        entity.year       = year;
        entity.start_date = startDate;
        entity.end_date   = endDate;

        await _db.SaveChangesAsync(ct);
        return true;
    }
    public async Task<bool> UpsertPolicyAsync(
        Guid semesterId,
        SemesterPolicyUpsertRequest req,
        CancellationToken ct)
    {
        var sem = await _db.semesters
            .Include(s => s.semester_policy)
            .SingleOrDefaultAsync(s => s.semester_id == semesterId, ct);

        if (sem is null) return false;

        var policy = sem.semester_policy;
        if (policy is null)
        {
            policy = new semester_policy
            {
                semester_id = sem.semester_id
            };
            _db.semester_policies.Add(policy);
            sem.semester_policy = policy;
        }

        policy.team_self_select_start  = req.TeamSelfSelectStart;
        policy.team_self_select_end    = req.TeamSelfSelectEnd;
        policy.team_suggest_start      = req.TeamSuggestStart;
        policy.topic_self_select_start = req.TopicSelfSelectStart;
        policy.topic_self_select_end   = req.TopicSelfSelectEnd;
        policy.topic_suggest_start     = req.TopicSuggestStart;
        policy.desired_group_size_min  = req.DesiredGroupSizeMin;
        policy.desired_group_size_max  = req.DesiredGroupSizeMax;

        await _db.SaveChangesAsync(ct);
        return true;
    }
    public async Task<bool> ActivateAsync(Guid semesterId, CancellationToken ct)
    {
        var target = await _db.semesters
            .SingleOrDefaultAsync(s => s.semester_id == semesterId, ct);

        if (target is null) return false;

        var actives = await _db.semesters
            .Where(s => s.is_active)
            .ToListAsync(ct);

        foreach (var s in actives)
        {
            s.is_active = false;
        }

        target.is_active = true;
        await _db.SaveChangesAsync(ct);

        return true;
    }

    public Task EnsureCurrentStateAsync(CancellationToken ct) => Task.CompletedTask;

    private static semester? FindCurrentSemester(IEnumerable<semester> semesters, DateOnly today)
        => semesters
            .Where(s => s.start_date.HasValue && s.end_date.HasValue && s.start_date.Value <= today && s.end_date.Value >= today)
            .OrderByDescending(s => s.year ?? s.start_date?.Year ?? s.end_date?.Year ?? 0)
            .ThenByDescending(s => SeasonIndex(s.season))
            .FirstOrDefault();

    private static (string Season, int Year, bool Changed) EnsureMetadata(semester sem, DateOnly referenceDate)
    {
        var changed = false;
        var season = NormalizeSeason(sem.season);
        if (string.IsNullOrEmpty(season))
        {
            var guess = GuessSeason(referenceDate);
            season = guess.Season;
            sem.season = season;
            if (!sem.year.HasValue)
                sem.year = guess.Year;
            changed = true;
        }

        var year = sem.year ?? sem.start_date?.Year ?? sem.end_date?.Year ?? referenceDate.Year;
        if (sem.year != year)
        {
            sem.year = year;
            changed = true;
        }

        if (!sem.start_date.HasValue || !sem.end_date.HasValue)
        {
            var (start, end) = DefaultWindow(season, year);
            if (!sem.start_date.HasValue)
            {
                sem.start_date = start;
                changed = true;
            }
            if (!sem.end_date.HasValue)
            {
                sem.end_date = end;
                changed = true;
            }
        }

        return (season, year, changed);
    }

    private bool EnsureSingleActive(IEnumerable<semester> semesters, semester current)
    {
        var changed = false;
        foreach (var sem in semesters)
        {
            var shouldBeActive = sem.semester_id == current.semester_id;
            if (sem.is_active != shouldBeActive)
            {
                sem.is_active = shouldBeActive;
                changed = true;
            }
        }
        return changed;
    }

    private bool EnsureUpcomingSemesters(ICollection<semester> semesters, string season, int year)
    {
        var changed = false;
        var sequence = BuildSequence(season, year, 3);
        foreach (var (nextSeason, nextYear) in sequence.Skip(1))
        {
            if (semesters.Any(s => SameSemester(s, nextSeason, nextYear)))
                continue;

            var entity = BuildSemester(nextSeason, nextYear, isActive: false);
            _db.semesters.Add(entity);
            semesters.Add(entity);
            changed = true;
        }

        return changed;
    }

    private static IReadOnlyList<(string Season, int Year)> BuildSequence(string startSeason, int startYear, int count)
    {
        var items = new List<(string Season, int Year)>(count) { (startSeason, startYear) };
        var cursorSeason = startSeason;
        var cursorYear = startYear;
        while (items.Count < count)
        {
            (cursorSeason, cursorYear) = NextSeason(cursorSeason, cursorYear);
            items.Add((cursorSeason, cursorYear));
        }

        return items;
    }

    private static (string Season, int Year) NextSeason(string season, int year)
        => NormalizeSeason(season) switch
        {
            "SPRING" => ("SUMMER", year),
            "SUMMER" => ("FALL", year),
            "FALL" => ("SPRING", year + 1),
            _ => ("SPRING", year)
        };

    private static (string Season, int Year) GuessSeason(DateOnly today)
        => today.Month <= 4
            ? ("SPRING", today.Year)
            : today.Month <= 8
                ? ("SUMMER", today.Year)
                : ("FALL", today.Year);

    private static string NormalizeSeason(string? season)
        => string.IsNullOrWhiteSpace(season) ? string.Empty : season.Trim().ToUpperInvariant();

    private static bool SameSemester(semester sem, string season, int year)
    {
        var normalized = NormalizeSeason(sem.season);
        if (!string.Equals(normalized, season, StringComparison.Ordinal))
            return false;

        var semYear = sem.year ?? sem.start_date?.Year ?? sem.end_date?.Year;
        return semYear == year;
    }

    private static semester BuildSemester(string season, int year, bool isActive)
    {
        var (start, end) = DefaultWindow(season, year);
        return new semester
        {
            semester_id = Guid.NewGuid(),
            season = season,
            year = year,
            start_date = start,
            end_date = end,
            is_active = isActive
        };
    }

    private static (DateOnly start, DateOnly end) DefaultWindow(string season, int year)
        => season switch
        {
            "SPRING" => (new DateOnly(year, 1, 1), new DateOnly(year, 4, 30)),
            "SUMMER" => (new DateOnly(year, 5, 1), new DateOnly(year, 8, 31)),
            "FALL" => (new DateOnly(year, 9, 1), new DateOnly(year, 12, 31)),
            _ => throw new ArgumentOutOfRangeException(nameof(season), $"Unknown season: {season}")
        };

    private static int SeasonIndex(string? season)
    {
        var normalized = NormalizeSeason(season);
        for (var i = 0; i < SeasonSequence.Length; i++)
        {
            if (SeasonSequence[i] == normalized) return i;
        }
        return -1;
    }
}
