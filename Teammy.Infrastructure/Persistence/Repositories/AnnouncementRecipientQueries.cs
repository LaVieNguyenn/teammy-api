using System;
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

    public async Task<IReadOnlyList<Guid>> ResolveTargetGroupIdsAsync(
        string scope,
        Guid semesterId,
        IReadOnlyList<Guid>? targetGroupIds,
        CancellationToken ct)
    {
        if (targetGroupIds is { Count: > 0 })
            return targetGroupIds.Distinct().ToList();

        var normalizedScope = scope?.Trim().ToLowerInvariant() ?? string.Empty;
        if (normalizedScope == AnnouncementScopes.GroupsWithoutTopic)
        {
            return await db.groups.AsNoTracking()
                .Where(g => g.semester_id == semesterId
                            && g.topic_id == null
                            && g.status != null
                            && g.status.ToLower() != "closed")
                .Select(g => g.group_id)
                .ToListAsync(ct);
        }

        if (normalizedScope == AnnouncementScopes.GroupsUnderstaffed)
        {
            return await db.groups.AsNoTracking()
                .Where(g => g.semester_id == semesterId
                            && g.status != null
                            && g.status.ToLower() != "closed"
                            && db.group_members.Count(x =>
                                x.group_id == g.group_id
                                && x.status != null
                                && ActiveStatuses.Contains(x.status.ToLower())) < g.max_members)
                .Select(g => g.group_id)
                .ToListAsync(ct);
        }

        return Array.Empty<Guid>();
    }

    public async Task<IReadOnlyList<Guid>> ResolveTargetUserIdsAsync(
        string scope,
        Guid semesterId,
        IReadOnlyList<Guid>? targetUserIds,
        CancellationToken ct)
    {
        var normalizedScope = scope?.Trim().ToLowerInvariant() ?? string.Empty;
        if (normalizedScope != AnnouncementScopes.StudentsWithoutGroup)
            return Array.Empty<Guid>();

        if (targetUserIds is { Count: > 0 })
        {
            var filter = targetUserIds.Distinct().ToList();
            return await (
                    from ur in db.user_roles.AsNoTracking()
                    join r in db.roles.AsNoTracking() on ur.role_id equals r.role_id
                    join u in db.users.AsNoTracking() on ur.user_id equals u.user_id
                    where filter.Contains(u.user_id)
                          && r.name.ToLower() == "student"
                          && u.is_active
                          && !string.IsNullOrWhiteSpace(u.email)
                          && !db.group_members.Any(gm => gm.user_id == u.user_id
                              && gm.semester_id == semesterId
                              && gm.status != null
                              && ActiveStatuses.Contains(gm.status.ToLower()))
                    select u.user_id)
                .Distinct()
                .ToListAsync(ct);
        }

        return await (
                from ur in db.user_roles.AsNoTracking()
                join r in db.roles.AsNoTracking() on ur.role_id equals r.role_id
                join u in db.users.AsNoTracking() on ur.user_id equals u.user_id
                where r.name.ToLower() == "student"
                      && u.is_active
                      && !string.IsNullOrWhiteSpace(u.email)
                      && !db.group_members.Any(gm => gm.user_id == u.user_id
                          && gm.semester_id == semesterId
                          && gm.status != null
                          && ActiveStatuses.Contains(gm.status.ToLower()))
                select u.user_id)
            .Distinct()
            .ToListAsync(ct);
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
            var s when s == AnnouncementScopes.Group && groupFilter is { Count: > 0 } => ProjectRecipients(QueryGroupUsers(groupFilter)),
            var s when s == AnnouncementScopes.GroupsWithoutTopic && semesterId.HasValue => ProjectRecipients(QueryGroupsWithoutTopic(semesterId.Value, groupFilter)),
            var s when s == AnnouncementScopes.GroupsUnderstaffed && semesterId.HasValue => ProjectRecipients(QueryUnderstaffedGroups(semesterId.Value, groupFilter)),
            var s when s == AnnouncementScopes.StudentsWithoutGroup && semesterId.HasValue => ProjectRecipients(QueryStudentsWithoutGroup(semesterId.Value, userFilter)),
            _ => null
        };
    }

    private static IQueryable<AnnouncementRecipient> ProjectRecipients(IQueryable<RecipientProjection> source)
        => source
            .Select(x => new { x.UserId, x.Email, x.DisplayName })
            .Distinct()
            .Select(x => new AnnouncementRecipient(
                x.UserId,
                x.Email!,
                x.DisplayName
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
            from u in db.users.AsNoTracking()
            where g.semester_id == semesterId
                  && (g.mentor_id == u.user_id
                      || (g.mentor_ids != null && g.mentor_ids.Contains(u.user_id)))
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
            from u in db.users.AsNoTracking()
            where g.group_id == groupId
                  && (g.mentor_id == u.user_id
                      || (g.mentor_ids != null && g.mentor_ids.Contains(u.user_id)))
                  && u.is_active
                  && !string.IsNullOrWhiteSpace(u.email)
            select new RecipientProjection(u.user_id, u.email!, u.display_name);

        return members.Union(mentors);
    }

    private IQueryable<RecipientProjection> QueryGroupUsers(IReadOnlySet<Guid> groupIds)
    {
        var members =
            from gm in db.group_members.AsNoTracking()
            join u in db.users.AsNoTracking() on gm.user_id equals u.user_id
            where groupIds.Contains(gm.group_id)
                  && ActiveStatuses.Contains(gm.status)
                  && u.is_active
                  && !string.IsNullOrWhiteSpace(u.email)
            select new RecipientProjection(u.user_id, u.email!, u.display_name);

        var mentors =
            from g in db.groups.AsNoTracking()
            from u in db.users.AsNoTracking()
            where groupIds.Contains(g.group_id)
                  && (g.mentor_id == u.user_id
                      || (g.mentor_ids != null && g.mentor_ids.Contains(u.user_id)))
                  && u.is_active
                  && !string.IsNullOrWhiteSpace(u.email)
            select new RecipientProjection(u.user_id, u.email!, u.display_name);

        return members.Union(mentors);
    }

    private IQueryable<RecipientProjection> QueryGroupsWithoutTopic(Guid semesterId, IReadOnlySet<Guid>? groupIds)
    {
        var query =
            from gm in db.group_members.AsNoTracking()
            join g in db.groups.AsNoTracking() on gm.group_id equals g.group_id
            join u in db.users.AsNoTracking() on gm.user_id equals u.user_id
            where g.semester_id == semesterId
                  && g.topic_id == null
                  && g.status != null
                  && g.status.ToLower() != "closed"
                  && gm.status != null
                  && gm.status.ToLower() == "leader"
                  && u.is_active
                  && !string.IsNullOrWhiteSpace(u.email)
            select new { g.group_id, u.user_id, u.email, u.display_name };

        if (groupIds is { Count: > 0 })
            query = query.Where(x => groupIds.Contains(x.group_id));

        return query.Select(x => new RecipientProjection(x.user_id, x.email!, x.display_name));
    }

    private IQueryable<RecipientProjection> QueryUnderstaffedGroups(Guid semesterId, IReadOnlySet<Guid>? groupIds)
    {
        var query =
            from gm in db.group_members.AsNoTracking()
            join g in db.groups.AsNoTracking() on gm.group_id equals g.group_id
            join u in db.users.AsNoTracking() on gm.user_id equals u.user_id
            where g.semester_id == semesterId
                  && g.status != null
                  && g.status.ToLower() != "closed"
                  && gm.status != null
                  && gm.status.ToLower() == "leader"
                  && u.is_active
                  && !string.IsNullOrWhiteSpace(u.email)
                  && db.group_members.Count(x =>
                      x.group_id == g.group_id
                      && x.status != null
                      && ActiveStatuses.Contains(x.status.ToLower())) < g.max_members
            select new { g.group_id, u.user_id, u.email, u.display_name };

        if (groupIds is { Count: > 0 })
            query = query.Where(x => groupIds.Contains(x.group_id));

        return query.Select(x => new RecipientProjection(x.user_id, x.email!, x.display_name));
    }

    private IQueryable<RecipientProjection> QueryStudentsWithoutGroup(Guid semesterId, IReadOnlySet<Guid>? userIds)
    {
        var query =
            from ur in db.user_roles.AsNoTracking()
            join r in db.roles.AsNoTracking() on ur.role_id equals r.role_id
            join u in db.users.AsNoTracking() on ur.user_id equals u.user_id
            where r.name.ToLower() == "student"
                  && u.is_active
                  && !string.IsNullOrWhiteSpace(u.email)
                  && !db.group_members.Any(gm => gm.user_id == u.user_id
                      && gm.semester_id == semesterId
                      && gm.status != null
                      && ActiveStatuses.Contains(gm.status.ToLower()))
            select new { u.user_id, u.email, u.display_name };

        if (userIds is { Count: > 0 })
            query = query.Where(x => userIds.Contains(x.user_id));

        return query.Select(x => new RecipientProjection(x.user_id, x.email!, x.display_name));
    }

    private sealed record RecipientProjection(Guid UserId, string Email, string? DisplayName);
}
