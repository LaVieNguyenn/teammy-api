using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using Teammy.Application.Announcements.Dtos;
using Teammy.Application.Common.Interfaces;
using Teammy.Infrastructure.Persistence;

namespace Teammy.Infrastructure.Persistence.Repositories;

public sealed class AnnouncementRecipientQueries(AppDbContext db) : IAnnouncementRecipientQueries
{
    private static readonly string[] ActiveStatuses = { "leader", "member" };

    public async Task<IReadOnlyList<AnnouncementRecipient>> ResolveRecipientsAsync(string scope, Guid? semesterId, string? targetRole, Guid? targetGroupId, CancellationToken ct)
    {
        scope = scope?.ToLowerInvariant() ?? string.Empty;
        targetRole = targetRole?.ToLowerInvariant();

        List<AnnouncementRecipient> recipients;
        if (scope == AnnouncementScopes.Global)
            recipients = await FetchAllActiveUsersAsync(ct);
        else if (scope == AnnouncementScopes.Semester && semesterId.HasValue)
            recipients = await FetchSemesterUsersAsync(semesterId.Value, ct);
        else if (scope == AnnouncementScopes.Role && !string.IsNullOrWhiteSpace(targetRole))
            recipients = await FetchRoleUsersAsync(targetRole!, ct);
        else if (scope == AnnouncementScopes.Group && targetGroupId.HasValue)
            recipients = await FetchGroupUsersAsync(targetGroupId.Value, ct);
        else if (scope == AnnouncementScopes.GroupsWithoutTopic && semesterId.HasValue)
            recipients = await FetchGroupsWithoutTopicAsync(semesterId.Value, ct);
        else if (scope == AnnouncementScopes.GroupsUnderstaffed && semesterId.HasValue)
            recipients = await FetchUnderstaffedGroupUsersAsync(semesterId.Value, ct);
        else if (scope == AnnouncementScopes.StudentsWithoutGroup && semesterId.HasValue)
            recipients = await FetchStudentsWithoutGroupAsync(semesterId.Value, ct);
        else
            recipients = new List<AnnouncementRecipient>();

        return recipients
            .GroupBy(r => r.UserId)
            .Select(g => g.First())
            .ToList();
    }

    private Task<List<AnnouncementRecipient>> FetchAllActiveUsersAsync(CancellationToken ct)
        => db.users.AsNoTracking()
            .Where(u => u.is_active && !string.IsNullOrWhiteSpace(u.email))
            .Select(u => new AnnouncementRecipient(u.user_id, u.email!, u.display_name))
            .ToListAsync(ct);

    private Task<List<AnnouncementRecipient>> FetchSemesterUsersAsync(Guid semesterId, CancellationToken ct)
    {
        var members =
            from gm in db.group_members.AsNoTracking()
            join u in db.users.AsNoTracking() on gm.user_id equals u.user_id
            where gm.semester_id == semesterId
                  && ActiveStatuses.Contains(gm.status)
                  && u.is_active
                  && !string.IsNullOrWhiteSpace(u.email)
            select new AnnouncementRecipient(u.user_id, u.email!, u.display_name);

        var mentors =
            from g in db.groups.AsNoTracking()
            join u in db.users.AsNoTracking() on g.mentor_id equals u.user_id
            where g.semester_id == semesterId
                  && g.mentor_id != null
                  && u.is_active
                  && !string.IsNullOrWhiteSpace(u.email)
            select new AnnouncementRecipient(u.user_id, u.email!, u.display_name);

        return members.Union(mentors).ToListAsync(ct);
    }

    private Task<List<AnnouncementRecipient>> FetchRoleUsersAsync(string targetRole, CancellationToken ct)
    {
        return (
            from ur in db.user_roles.AsNoTracking()
            join r in db.roles.AsNoTracking() on ur.role_id equals r.role_id
            join u in db.users.AsNoTracking() on ur.user_id equals u.user_id
            where r.name.ToLower() == targetRole
                  && u.is_active
                  && !string.IsNullOrWhiteSpace(u.email)
            select new AnnouncementRecipient(u.user_id, u.email!, u.display_name)
        ).ToListAsync(ct);
    }

    private Task<List<AnnouncementRecipient>> FetchGroupUsersAsync(Guid groupId, CancellationToken ct)
    {
        var members =
            from gm in db.group_members.AsNoTracking()
            join u in db.users.AsNoTracking() on gm.user_id equals u.user_id
            where gm.group_id == groupId
                  && ActiveStatuses.Contains(gm.status)
                  && u.is_active
                  && !string.IsNullOrWhiteSpace(u.email)
            select new AnnouncementRecipient(u.user_id, u.email!, u.display_name);

        var mentor =
            from g in db.groups.AsNoTracking()
            join u in db.users.AsNoTracking() on g.mentor_id equals u.user_id
            where g.group_id == groupId
                  && g.mentor_id != null
                  && u.is_active
                  && !string.IsNullOrWhiteSpace(u.email)
            select new AnnouncementRecipient(u.user_id, u.email!, u.display_name);

        return members.Union(mentor).ToListAsync(ct);
    }

    private Task<List<AnnouncementRecipient>> FetchGroupsWithoutTopicAsync(Guid semesterId, CancellationToken ct)
    {
        var members =
            from gm in db.group_members.AsNoTracking()
            join g in db.groups.AsNoTracking() on gm.group_id equals g.group_id
            join u in db.users.AsNoTracking() on gm.user_id equals u.user_id
            where g.semester_id == semesterId
                  && g.topic_id == null
                  && g.status != "closed"
                  && ActiveStatuses.Contains(gm.status)
                  && u.is_active
                  && !string.IsNullOrWhiteSpace(u.email)
            select new AnnouncementRecipient(u.user_id, u.email!, u.display_name);

        var mentors =
            from g in db.groups.AsNoTracking()
            join u in db.users.AsNoTracking() on g.mentor_id equals u.user_id
            where g.semester_id == semesterId
                  && g.topic_id == null
                  && g.status != "closed"
                  && g.mentor_id != null
                  && u.is_active
                  && !string.IsNullOrWhiteSpace(u.email)
            select new AnnouncementRecipient(u.user_id, u.email!, u.display_name);

        return members.Union(mentors).ToListAsync(ct);
    }

    private Task<List<AnnouncementRecipient>> FetchUnderstaffedGroupUsersAsync(Guid semesterId, CancellationToken ct)
    {
        var members =
            from gm in db.group_members.AsNoTracking()
            join g in db.groups.AsNoTracking() on gm.group_id equals g.group_id
            join u in db.users.AsNoTracking() on gm.user_id equals u.user_id
            where g.semester_id == semesterId
                  && g.status != "closed"
                  && ActiveStatuses.Contains(gm.status)
                  && u.is_active
                  && !string.IsNullOrWhiteSpace(u.email)
                  && db.group_members.Count(x => x.group_id == g.group_id && ActiveStatuses.Contains(x.status)) < g.max_members
            select new AnnouncementRecipient(u.user_id, u.email!, u.display_name);

        var mentors =
            from g in db.groups.AsNoTracking()
            join u in db.users.AsNoTracking() on g.mentor_id equals u.user_id
            where g.semester_id == semesterId
                  && g.status != "closed"
                  && g.mentor_id != null
                  && u.is_active
                  && !string.IsNullOrWhiteSpace(u.email)
                  && db.group_members.Count(x => x.group_id == g.group_id && ActiveStatuses.Contains(x.status)) < g.max_members
            select new AnnouncementRecipient(u.user_id, u.email!, u.display_name);

        return members.Union(mentors).ToListAsync(ct);
    }

    private Task<List<AnnouncementRecipient>> FetchStudentsWithoutGroupAsync(Guid semesterId, CancellationToken ct)
    {
        var students =
            from ur in db.user_roles.AsNoTracking()
            join r in db.roles.AsNoTracking() on ur.role_id equals r.role_id
            join u in db.users.AsNoTracking() on ur.user_id equals u.user_id
            where r.name.ToLower() == "student"
                  && u.is_active
                  && !string.IsNullOrWhiteSpace(u.email)
                  && !db.group_members.Any(gm => gm.user_id == u.user_id
                      && gm.semester_id == semesterId
                      && ActiveStatuses.Contains(gm.status))
            select new AnnouncementRecipient(u.user_id, u.email!, u.display_name);

        return students.ToListAsync(ct);
    }
}
