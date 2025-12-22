using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using Teammy.Application.Announcements.Dtos;
using Teammy.Application.Common.Dtos;
using Teammy.Application.Common.Interfaces;
using Teammy.Infrastructure.Persistence;

namespace Teammy.Infrastructure.Persistence.Repositories;

public sealed class AnnouncementRecipientQueries(AppDbContext db) : IAnnouncementRecipientQueries
{
    private static readonly string[] ActiveStatuses = { "leader", "member" };

    public async Task<IReadOnlyList<AnnouncementRecipient>> ResolveRecipientsAsync(
        string scope,
        Guid? semesterId,
        string? targetRole,
        Guid? targetGroupId,
        IReadOnlyList<Guid>? targetGroupIds,
        IReadOnlyList<Guid>? targetUserIds,
        CancellationToken ct)
    {
        var query = BuildScopeQuery(scope, semesterId, targetRole, targetGroupId, targetGroupIds, targetUserIds);
        if (query is null)
            return Array.Empty<AnnouncementRecipient>();

        return await query.ToListAsync(ct);
    }

    public async Task<PaginatedResult<AnnouncementRecipient>> ListRecipientsAsync(
        string scope,
        Guid? semesterId,
        string? targetRole,
        Guid? targetGroupId,
        IReadOnlyList<Guid>? targetGroupIds,
        IReadOnlyList<Guid>? targetUserIds,
        int page,
        int pageSize,
        CancellationToken ct)
    {
        var query = BuildScopeQuery(scope, semesterId, targetRole, targetGroupId, targetGroupIds, targetUserIds);
        if (query is null)
            return new PaginatedResult<AnnouncementRecipient>(0, page, pageSize, Array.Empty<AnnouncementRecipient>());

        var total = await query.CountAsync(ct);
        if (total == 0)
            return new PaginatedResult<AnnouncementRecipient>(0, page, pageSize, Array.Empty<AnnouncementRecipient>());

        var skip = (page - 1) * pageSize;
        var items = await query
            .OrderBy(r => r.DisplayName ?? r.Email)
            .ThenBy(r => r.Email)
            .Skip(skip)
            .Take(pageSize)
            .ToListAsync(ct);

        return new PaginatedResult<AnnouncementRecipient>(total, page, pageSize, items);
    }

    private IQueryable<AnnouncementRecipient>? BuildScopeQuery(
        string scope,
        Guid? semesterId,
        string? targetRole,
        Guid? targetGroupId,
        IReadOnlyList<Guid>? targetGroupIds,
        IReadOnlyList<Guid>? targetUserIds)
    {
        var normalizedScope = scope?.Trim().ToLowerInvariant() ?? string.Empty;
        var normalizedRole = targetRole?.Trim().ToLowerInvariant();
        var groupFilter = targetGroupIds is { Count: > 0 }
            ? targetGroupIds.Distinct().ToHashSet()
            : null;
        var userFilter = targetUserIds is { Count: > 0 }
            ? targetUserIds.Distinct().ToHashSet()
            : null;

        return normalizedScope switch
        {
            var s when s == AnnouncementScopes.Global => ProjectRecipients(QueryAllActiveUsers()),
            var s when s == AnnouncementScopes.Semester && semesterId.HasValue => ProjectRecipients(QuerySemesterUsers(semesterId.Value)),
            var s when s == AnnouncementScopes.Role && !string.IsNullOrWhiteSpace(normalizedRole) => ProjectRecipients(QueryRoleUsers(normalizedRole!)),
            var s when s == AnnouncementScopes.Group && targetGroupId.HasValue => ProjectRecipients(QueryGroupUsers(targetGroupId.Value)),
            var s when s == AnnouncementScopes.GroupsWithoutTopic && semesterId.HasValue => ProjectRecipients(QueryGroupsWithoutTopic(semesterId.Value, groupFilter)),
            var s when s == AnnouncementScopes.GroupsUnderstaffed && semesterId.HasValue => ProjectRecipients(QueryUnderstaffedGroups(semesterId.Value, groupFilter)),
            var s when s == AnnouncementScopes.StudentsWithoutGroup && semesterId.HasValue => ProjectRecipients(QueryStudentsWithoutGroup(semesterId.Value, userFilter)),
            _ => null
        };
    }

    private static IQueryable<AnnouncementRecipient> ProjectRecipients(IQueryable<RecipientProjection> source)
        => source
            .GroupBy(x => x.UserId)
            .Select(g => new AnnouncementRecipient(
                g.Key,
                g.Max(x => x.Email)!,
                g.Max(x => x.DisplayName)
            ));

    private IQueryable<RecipientProjection> QueryAllActiveUsers()
        => db.users.AsNoTracking()
            .Where(u => u.is_active && !string.IsNullOrWhiteSpace(u.email))
            .Select(u => new RecipientProjection(u.user_id, u.email!, u.display_name));

    private IQueryable<RecipientProjection> QuerySemesterUsers(Guid semesterId)
    {
        var members =
            from gm in db.group_members.AsNoTracking()
            join u in db.users.AsNoTracking() on gm.user_id equals u.user_id
            where gm.semester_id == semesterId
                  && ActiveStatuses.Contains(gm.status)
                  && u.is_active
                  && !string.IsNullOrWhiteSpace(u.email)
            select new RecipientProjection(u.user_id, u.email!, u.display_name);

        var mentors =
            from g in db.groups.AsNoTracking()
            join u in db.users.AsNoTracking() on g.mentor_id equals u.user_id
            where g.semester_id == semesterId
                  && g.mentor_id != null
                  && u.is_active
                  && !string.IsNullOrWhiteSpace(u.email)
            select new RecipientProjection(u.user_id, u.email!, u.display_name);

        return members.Union(mentors);
    }

    private IQueryable<RecipientProjection> QueryRoleUsers(string targetRole)
        => from ur in db.user_roles.AsNoTracking()
           join r in db.roles.AsNoTracking() on ur.role_id equals r.role_id
           join u in db.users.AsNoTracking() on ur.user_id equals u.user_id
           where r.name.ToLower() == targetRole
                 && u.is_active
                 && !string.IsNullOrWhiteSpace(u.email)
           select new RecipientProjection(u.user_id, u.email!, u.display_name);

    private IQueryable<RecipientProjection> QueryGroupUsers(Guid groupId)
    {
        var members =
            from gm in db.group_members.AsNoTracking()
            join u in db.users.AsNoTracking() on gm.user_id equals u.user_id
            where gm.group_id == groupId
                  && ActiveStatuses.Contains(gm.status)
                  && u.is_active
                  && !string.IsNullOrWhiteSpace(u.email)
            select new RecipientProjection(u.user_id, u.email!, u.display_name);

        var mentors =
            from g in db.groups.AsNoTracking()
            join u in db.users.AsNoTracking() on g.mentor_id equals u.user_id
            where g.group_id == groupId
                  && g.mentor_id != null
                  && u.is_active
                  && !string.IsNullOrWhiteSpace(u.email)
            select new RecipientProjection(u.user_id, u.email!, u.display_name);

        return members.Union(mentors);
    }

    private IQueryable<RecipientProjection> QueryGroupsWithoutTopic(Guid semesterId, IReadOnlySet<Guid>? groupIds)
        => from gm in db.group_members.AsNoTracking()
           join g in db.groups.AsNoTracking() on gm.group_id equals g.group_id
           join u in db.users.AsNoTracking() on gm.user_id equals u.user_id
           where g.semester_id == semesterId
                 && g.topic_id == null
                 && g.status != "closed"
                 && gm.status == "leader"
                 && u.is_active
                 && !string.IsNullOrWhiteSpace(u.email)
                 && (groupIds == null || groupIds.Contains(g.group_id))
           select new RecipientProjection(u.user_id, u.email!, u.display_name);

    private IQueryable<RecipientProjection> QueryUnderstaffedGroups(Guid semesterId, IReadOnlySet<Guid>? groupIds)
        => from gm in db.group_members.AsNoTracking()
           join g in db.groups.AsNoTracking() on gm.group_id equals g.group_id
           join u in db.users.AsNoTracking() on gm.user_id equals u.user_id
           where g.semester_id == semesterId
                 && g.status != "closed"
                 && gm.status == "leader"
                 && u.is_active
                 && !string.IsNullOrWhiteSpace(u.email)
                 && db.group_members.Count(x => x.group_id == g.group_id && ActiveStatuses.Contains(x.status)) < g.max_members
                 && (groupIds == null || groupIds.Contains(g.group_id))
           select new RecipientProjection(u.user_id, u.email!, u.display_name);

    private IQueryable<RecipientProjection> QueryStudentsWithoutGroup(Guid semesterId, IReadOnlySet<Guid>? userIds)
        => from ur in db.user_roles.AsNoTracking()
           join r in db.roles.AsNoTracking() on ur.role_id equals r.role_id
           join u in db.users.AsNoTracking() on ur.user_id equals u.user_id
           where r.name.ToLower() == "student"
                 && u.is_active
                 && !string.IsNullOrWhiteSpace(u.email)
                 && (userIds == null || userIds.Contains(u.user_id))
                 && !db.group_members.Any(gm => gm.user_id == u.user_id
                     && gm.semester_id == semesterId
                     && ActiveStatuses.Contains(gm.status))
           select new RecipientProjection(u.user_id, u.email!, u.display_name);

    private sealed record RecipientProjection(Guid UserId, string Email, string? DisplayName);
}
