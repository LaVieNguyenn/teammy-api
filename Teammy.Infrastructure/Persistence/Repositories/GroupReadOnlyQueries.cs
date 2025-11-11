using Microsoft.EntityFrameworkCore;
using Teammy.Application.Common.Interfaces;
using Teammy.Application.Groups.Dtos;
using Teammy.Infrastructure.Persistence;
using Teammy.Infrastructure.Persistence.Models;

namespace Teammy.Infrastructure.Persistence.Repositories;

public sealed class GroupReadOnlyQueries(AppDbContext db) : IGroupReadOnlyQueries
{
    public async Task<Guid?> GetActiveSemesterIdAsync(CancellationToken ct)
        => await db.semesters.Where(s => s.is_active).Select(s => (Guid?)s.semester_id).FirstOrDefaultAsync(ct);

    public async Task<IReadOnlyList<GroupSummaryDto>> ListGroupsAsync(string? status, Guid? majorId, Guid? topicId, CancellationToken ct)
    {
        var activeStatuses = new[] { "member", "leader" };

        var q = from g in db.groups.AsNoTracking()
                select new { g };

        if (!string.IsNullOrWhiteSpace(status)) q = q.Where(x => x.g.status == status);
        if (majorId.HasValue) q = q.Where(x => x.g.major_id == majorId);
        if (topicId.HasValue) q = q.Where(x => x.g.topic_id == topicId);

        var list = await q
            .Select(x => new GroupSummaryDto(
                x.g.group_id,
                x.g.semester_id,
                x.g.name,
                x.g.description,
                x.g.status,
                x.g.max_members,
                x.g.topic_id,
                x.g.major_id,
                db.group_members.Count(m => m.group_id == x.g.group_id && activeStatuses.Contains(m.status))
            ))
            .ToListAsync(ct);

        return list;
    }

    public async Task<GroupDetailDto?> GetGroupAsync(Guid id, CancellationToken ct)
    {
        var activeStatuses = new[] { "member", "leader" };
        return await db.groups.AsNoTracking()
            .Where(g => g.group_id == id)
            .Select(g => new GroupDetailDto(
                g.group_id,
                g.semester_id,
                g.name,
                g.description,
                g.status,
                g.max_members,
                g.topic_id,
                g.major_id,
                db.group_members.Count(m => m.group_id == g.group_id && activeStatuses.Contains(m.status))
            ))
            .FirstOrDefaultAsync(ct);
    }

    public async Task<IReadOnlyList<JoinRequestDto>> GetPendingJoinRequestsAsync(Guid groupId, CancellationToken ct)
    {
        var q =
            from m in db.group_members.AsNoTracking()
            join u in db.users.AsNoTracking() on m.user_id equals u.user_id
            where m.group_id == groupId && m.status == "pending"
            orderby m.joined_at
            select new JoinRequestDto(m.group_member_id, u.user_id, u.email!, u.display_name!, m.joined_at);

        return await q.ToListAsync(ct);
    }

    public Task<bool> IsLeaderAsync(Guid groupId, Guid userId, CancellationToken ct)
        => db.group_members.AsNoTracking().AnyAsync(x => x.group_id == groupId && x.user_id == userId && x.status == "leader", ct);

    public Task<bool> HasActiveMembershipInSemesterAsync(Guid userId, Guid semesterId, CancellationToken ct)
        => db.group_members.AsNoTracking().AnyAsync(x => x.user_id == userId && x.semester_id == semesterId && (x.status == "pending" || x.status == "member" || x.status == "leader"), ct);

    public async Task<(int MaxMembers, int ActiveCount)> GetGroupCapacityAsync(Guid groupId, CancellationToken ct)
    {
        var g = await db.groups.AsNoTracking().FirstOrDefaultAsync(x => x.group_id == groupId, ct)
            ?? throw new KeyNotFoundException("Group not found");
        var activeCount = await db.group_members.AsNoTracking().CountAsync(x => x.group_id == groupId && (x.status == "member" || x.status == "leader"), ct);
        return (g.max_members, activeCount);
    }

    public async Task<Guid?> GetLeaderGroupIdAsync(Guid userId, Guid semesterId, CancellationToken ct)
    {
        var groupId = await db.group_members.AsNoTracking()
            .Where(m => m.user_id == userId && m.semester_id == semesterId && m.status == "leader")
            .Select(m => (Guid?)m.group_id)
            .FirstOrDefaultAsync(ct);
        return groupId;
    }

    public async Task<IReadOnlyList<Teammy.Application.Groups.Dtos.MyGroupDto>> ListMyGroupsAsync(Guid userId, Guid? semesterId, CancellationToken ct)
    {
        Guid? semId = semesterId;
        if (!semId.HasValue)
            semId = await db.semesters.AsNoTracking().Where(s => s.is_active).Select(s => (Guid?)s.semester_id).FirstOrDefaultAsync(ct);
        if (!semId.HasValue) return Array.Empty<Teammy.Application.Groups.Dtos.MyGroupDto>();

        var activeStatuses = new[] { "member", "leader" };

        var q = from m in db.group_members.AsNoTracking()
                join g in db.groups.AsNoTracking() on m.group_id equals g.group_id
                where m.user_id == userId && g.semester_id == semId.Value
                select new Teammy.Application.Groups.Dtos.MyGroupDto(
                    g.group_id,
                    g.semester_id,
                    g.name,
                    g.status,
                    g.max_members,
                    db.group_members.Count(x => x.group_id == g.group_id && activeStatuses.Contains(x.status)),
                    m.status
                );
        return await q.ToListAsync(ct);
    }

    public async Task<IReadOnlyList<Teammy.Application.Groups.Dtos.GroupMemberDto>> ListActiveMembersAsync(Guid groupId, CancellationToken ct)
    {
        var q = from m in db.group_members.AsNoTracking()
                join u in db.users.AsNoTracking() on m.user_id equals u.user_id
                where m.group_id == groupId && (m.status == "member" || m.status == "leader")
                orderby m.status descending, m.joined_at
                select new Teammy.Application.Groups.Dtos.GroupMemberDto(
                    u.user_id,
                    u.email!,
                    u.display_name!,
                    m.status,
                    m.joined_at,
                    u.avatar_url
                );
        return await q.ToListAsync(ct);
    }

    public async Task<Teammy.Application.Groups.Dtos.UserGroupCheckDto> CheckUserGroupAsync(Guid userId, Guid? semesterId, bool includePending, CancellationToken ct)
    {
        Guid? semId = semesterId;
        if (!semId.HasValue)
            semId = await db.semesters.AsNoTracking().Where(s => s.is_active).Select(s => (Guid?)s.semester_id).FirstOrDefaultAsync(ct);

        if (!semId.HasValue)
            return new Teammy.Application.Groups.Dtos.UserGroupCheckDto(false, Guid.Empty, null, null);

        var statuses = includePending ? new[] { "pending", "member", "leader" } : new[] { "member", "leader" };

        var row = await db.group_members.AsNoTracking()
            .Where(m => m.user_id == userId && m.semester_id == semId.Value && statuses.Contains(m.status))
            .Select(m => new { m.group_id, m.status })
            .FirstOrDefaultAsync(ct);

        return row is null
            ? new Teammy.Application.Groups.Dtos.UserGroupCheckDto(false, semId.Value, null, null)
            : new Teammy.Application.Groups.Dtos.UserGroupCheckDto(true, semId.Value, row.group_id, row.status);
    }

    public Task<(Guid SemesterId, string? Season, int? Year, DateOnly? StartDate, DateOnly? EndDate, bool IsActive)?> GetSemesterAsync(Guid semesterId, CancellationToken ct)
        => db.semesters.AsNoTracking()
            .Where(s => s.semester_id == semesterId)
            .Select(s => new ValueTuple<Guid, string?, int?, DateOnly?, DateOnly?, bool>(s.semester_id, s.season, s.year, s.start_date, s.end_date, s.is_active))
            .FirstOrDefaultAsync(ct)
            .ContinueWith(t => t.Result == default ? (ValueTuple<Guid, string?, int?, DateOnly?, DateOnly?, bool>?)null : t.Result, ct);

    public Task<(Guid MajorId, string MajorName)?> GetMajorAsync(Guid majorId, CancellationToken ct)
        => db.majors.AsNoTracking()
            .Where(m => m.major_id == majorId)
            .Select(m => new ValueTuple<Guid, string>(m.major_id, m.major_name))
            .FirstOrDefaultAsync(ct)
            .ContinueWith(t => t.Result == default ? (ValueTuple<Guid, string>?)null : t.Result, ct);

    public Task<bool> GroupNameExistsAsync(Guid semesterId, string name, Guid? excludeGroupId, CancellationToken ct)
        => db.groups.AsNoTracking()
            .AnyAsync(g => g.semester_id == semesterId && g.name == name && (!excludeGroupId.HasValue || g.group_id != excludeGroupId.Value), ct);
}
