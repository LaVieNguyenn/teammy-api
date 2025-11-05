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
}
