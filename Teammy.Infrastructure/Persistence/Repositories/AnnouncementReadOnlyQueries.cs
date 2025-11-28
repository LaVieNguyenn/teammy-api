using Microsoft.EntityFrameworkCore;
using Teammy.Application.Announcements.Dtos;
using Teammy.Application.Common.Interfaces;
using Teammy.Infrastructure.Persistence;
using Teammy.Infrastructure.Persistence.Models;

namespace Teammy.Infrastructure.Persistence.Repositories;

public sealed class AnnouncementReadOnlyQueries(AppDbContext db) : IAnnouncementReadOnlyQueries
{
    public async Task<IReadOnlyList<AnnouncementDto>> ListForUserAsync(Guid userId, AnnouncementFilter filter, CancellationToken ct)
    {
        var ctx = await BuildAccessContextAsync(userId, ct);
        var now = DateTime.UtcNow;
        var query = ApplyAccessFilter(ctx);

        if (!filter.IncludeExpired)
            query = query.Where(a => !a.expire_at.HasValue || a.expire_at >= now);
        if (filter.PinnedOnly)
            query = query.Where(a => a.pinned);

        var queryWithAuthor =
            from a in query
            join u in db.users.AsNoTracking() on a.created_by equals u.user_id
            orderby a.pinned descending, a.publish_at descending
            select new AnnouncementDto(
                a.announcement_id,
                a.semester_id,
                a.scope,
                a.target_role,
                a.target_group_id,
                a.title,
                a.content,
                a.pinned,
                a.publish_at,
                a.expire_at,
                a.created_by,
                u.display_name ?? u.email ?? string.Empty);

        return await queryWithAuthor.ToListAsync(ct);
    }

    public async Task<AnnouncementDto?> GetForUserAsync(Guid announcementId, Guid userId, CancellationToken ct)
    {
        var ctx = await BuildAccessContextAsync(userId, ct);

        var query =
            from a in ApplyAccessFilter(ctx)
            where a.announcement_id == announcementId
            join u in db.users.AsNoTracking() on a.created_by equals u.user_id
            select new AnnouncementDto(
                a.announcement_id,
                a.semester_id,
                a.scope,
                a.target_role,
                a.target_group_id,
                a.title,
                a.content,
                a.pinned,
                a.publish_at,
                a.expire_at,
                a.created_by,
                u.display_name ?? u.email ?? string.Empty);

        return await query.FirstOrDefaultAsync(ct);
    }

    private IQueryable<announcement> ApplyAccessFilter(AnnouncementAccessContext ctx)
    {
        var roles = ctx.Roles.Select(r => r.ToLowerInvariant()).ToList();
        var groupIds = ctx.GroupIds.ToList();
        var semesterIds = ctx.SemesterIds.ToList();

        return db.announcements.AsNoTracking().Where(a =>
            a.scope == AnnouncementScopes.Global
            || (a.scope == AnnouncementScopes.Role && a.target_role != null && roles.Contains(a.target_role.ToLower()))
            || (a.scope == AnnouncementScopes.Group && a.target_group_id.HasValue && groupIds.Contains(a.target_group_id.Value))
            || (a.scope == AnnouncementScopes.Semester && a.semester_id.HasValue && semesterIds.Contains(a.semester_id.Value))
        );
    }

    private async Task<AnnouncementAccessContext> BuildAccessContextAsync(Guid userId, CancellationToken ct)
    {
        var roles = await (
                from ur in db.user_roles
                join r in db.roles on ur.role_id equals r.role_id
                where ur.user_id == userId
                select r.name.ToLower())
            .ToListAsync(ct);

        var members = await db.group_members
            .Where(gm => gm.user_id == userId && (gm.status == "leader" || gm.status == "member"))
            .Select(gm => new { gm.group_id, gm.semester_id })
            .ToListAsync(ct);

        var mentorGroups = await db.groups
            .Where(g => g.mentor_id == userId)
            .Select(g => new { g.group_id, g.semester_id })
            .ToListAsync(ct);

        var groupSet = members.Select(m => m.group_id).Concat(mentorGroups.Select(m => m.group_id)).ToHashSet();
        var semesterSet = members.Select(m => m.semester_id).Concat(mentorGroups.Select(m => m.semester_id)).ToHashSet();
        var roleSet = roles.ToHashSet(StringComparer.OrdinalIgnoreCase);

        return new AnnouncementAccessContext(roleSet, groupSet, semesterSet);
    }

    private sealed record AnnouncementAccessContext(
        HashSet<string> Roles,
        HashSet<Guid> GroupIds,
        HashSet<Guid> SemesterIds
    );
}
