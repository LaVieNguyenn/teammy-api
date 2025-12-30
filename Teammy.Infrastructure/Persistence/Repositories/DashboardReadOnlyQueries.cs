using System;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Teammy.Application.Common.Interfaces;
using Teammy.Application.Dashboard.Dtos;
using Teammy.Infrastructure.Persistence;

namespace Teammy.Infrastructure.Persistence.Repositories;

public sealed class DashboardReadOnlyQueries(AppDbContext db) : IDashboardReadOnlyQueries
{
    public async Task<DashboardStatsDto> GetStatsAsync(Guid? semesterId, CancellationToken ct)
    {
        var usersTotal = await db.users.CountAsync(ct);
        var usersActive = await db.users.CountAsync(u => u.is_active, ct);

        var topicsQuery = db.topics.AsNoTracking();
        var groupsQuery = db.groups.AsNoTracking();
        var postsQuery = db.recruitment_posts.AsNoTracking();

        if (semesterId.HasValue)
        {
            topicsQuery = topicsQuery.Where(t => t.semester_id == semesterId.Value);
            groupsQuery = groupsQuery.Where(g => g.semester_id == semesterId.Value);
            postsQuery = postsQuery.Where(p => p.semester_id == semesterId.Value);
        }

        var topicsTotal = await topicsQuery.CountAsync(ct);
        var topicsOpen = await topicsQuery.CountAsync(t => t.status == "open", ct);
        var groupsTotal = await groupsQuery.CountAsync(ct);
        var groupsRecruiting = await groupsQuery.CountAsync(g => g.status == "recruiting", ct);
        var groupsActive = await groupsQuery.CountAsync(g => g.status == "active", ct);
        var postsTotal = await postsQuery.CountAsync(ct);
        var postsGroup = await postsQuery.CountAsync(p => p.post_type == "group_hiring", ct);
        var postsIndividual = await postsQuery.CountAsync(p => p.post_type == "individual", ct);

        return new DashboardStatsDto(
            usersTotal,
            usersActive,
            topicsTotal,
            topicsOpen,
            groupsTotal,
            groupsRecruiting,
            groupsActive,
            postsTotal,
            postsGroup,
            postsIndividual);
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
        var groupsWithoutTopic = await groupsQuery.Where(g => g.topic_id == null).CountAsync(ct);

        // Groups that still have room for more members.
        var memberStatuses = new[] { "member", "leader", "pending" };
        var groupsWithoutMember = await groupsQuery
            .Where(g => db.group_members
                .AsNoTracking()
                .Where(gm => gm.group_id == g.group_id && memberStatuses.Contains(gm.status))
                .Count() < g.max_members)
            .CountAsync(ct);

        var membershipStatuses = new[] { "member", "leader", "pending" };
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
        if (resolvedSemesterId.HasValue)
        {
            var semesterStudentIds = db.student_semesters
                .AsNoTracking()
                .Where(ss => ss.semester_id == resolvedSemesterId.Value)
                .Select(ss => ss.user_id)
                .Distinct();
            studentUserIdsQuery = studentUserIdsQuery.Where(id => semesterStudentIds.Contains(id));
        }

        var studentsWithoutGroup = await studentUserIdsQuery
            .Where(studentId => !memberUserIdsQuery.Contains(studentId))
            .CountAsync(ct);

        return new ModeratorDashboardStatsDto(
            totalGroups,
            groupsWithoutTopic,
            groupsWithoutMember,
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
