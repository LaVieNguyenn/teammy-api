using System;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Teammy.Application.Common.Interfaces;
using Teammy.Application.Dashboard.Dtos;
using Teammy.Infrastructure.Persistence;

namespace Teammy.Infrastructure.Persistence.Repositories;

public sealed class DashboardReadOnlyQueries(AppDbContext db) : IDashboardReadOnlyQueries
{
    public async Task<DashboardStatsDto> GetStatsAsync(CancellationToken ct)
    {
        return new DashboardStatsDto(
            await db.users.CountAsync(ct),
            await db.users.CountAsync(u => u.is_active, ct),
            await db.topics.CountAsync(ct),
            await db.topics.CountAsync(t => t.status == "open", ct),
            await db.groups.CountAsync(ct),
            await db.groups.CountAsync(g => g.status == "recruiting", ct),
            await db.groups.CountAsync(g => g.status == "active", ct),
            await db.recruitment_posts.CountAsync(ct),
            await db.recruitment_posts.CountAsync(p => p.post_type == "group_hiring", ct),
            await db.recruitment_posts.CountAsync(p => p.post_type == "individual", ct));
    }

    public async Task<ModeratorDashboardStatsDto> GetModeratorStatsAsync(Guid? semesterId, CancellationToken ct)
    {
        Guid? resolvedSemesterId = semesterId;
        string? semesterLabel = null;

        if (resolvedSemesterId.HasValue)
        {
            var info = await db.semesters.AsNoTracking()
                .Where(s => s.semester_id == resolvedSemesterId.Value)
                .Select(s => new { s.semester_id, s.season, s.year })
                .FirstOrDefaultAsync(ct);

            if (info is null)
            {
                resolvedSemesterId = null;
            }
            else
            {
                semesterLabel = BuildSemesterLabel(info.season, info.year);
            }
        }

        if (!resolvedSemesterId.HasValue)
        {
            var active = await db.semesters.AsNoTracking()
                .Where(s => s.is_active)
                .OrderByDescending(s => s.start_date ?? DateOnly.MinValue)
                .ThenByDescending(s => s.year)
                .Select(s => new { s.semester_id, s.season, s.year })
                .FirstOrDefaultAsync(ct);

            if (active is not null)
            {
                resolvedSemesterId = active.semester_id;
                semesterLabel = BuildSemesterLabel(active.season, active.year);
            }
        }

        var groupsQuery = db.groups
            .AsNoTracking()
            .Where(g => g.status != "archived");
        if (resolvedSemesterId.HasValue)
            groupsQuery = groupsQuery.Where(g => g.semester_id == resolvedSemesterId.Value);

        var totalGroups = await groupsQuery.CountAsync(ct);
        var groupsMissingTopic = await groupsQuery.Where(g => g.topic_id == null).CountAsync(ct);
        var groupsMissingMentor = await groupsQuery.Where(g => g.topic_id != null && g.mentor_id == null).CountAsync(ct);

        var membershipStatuses = new[] { "member", "leader" };
        var membershipQuery = db.group_members
            .AsNoTracking()
            .Where(gm => membershipStatuses.Contains(gm.status));
        if (resolvedSemesterId.HasValue)
            membershipQuery = membershipQuery.Where(gm => gm.semester_id == resolvedSemesterId.Value);
        var memberUserIdsQuery = membershipQuery
            .Select(gm => gm.user_id)
            .Distinct();

        var studentUserIdsQuery = db.user_roles
            .AsNoTracking()
            .Where(ur => ur.role.name.ToLower() == "student")
            .Select(ur => ur.user_id)
            .Distinct();

        var studentsWithoutGroup = await studentUserIdsQuery
            .Where(studentId => !memberUserIdsQuery.Contains(studentId))
            .CountAsync(ct);

        return new ModeratorDashboardStatsDto(
            totalGroups,
            groupsMissingTopic,
            groupsMissingMentor,
            studentsWithoutGroup,
            resolvedSemesterId,
            semesterLabel);
    }

    private static string? BuildSemesterLabel(string? season, int? year)
    {
        if (string.IsNullOrWhiteSpace(season) && !year.HasValue)
            return null;

        return year.HasValue
            ? $"{season ?? string.Empty} {year.Value}".Trim()
            : season;
    }
}
