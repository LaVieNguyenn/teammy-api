using Microsoft.EntityFrameworkCore;
using Teammy.Application.Common.Interfaces;
using Teammy.Infrastructure.Persistence;
using Teammy.Infrastructure.Persistence.Models;

namespace Teammy.Infrastructure.Persistence.Repositories;

public sealed class GroupRepository(AppDbContext db) : IGroupRepository
{
    public async Task<Guid> CreateGroupAsync(Guid semesterId, Guid? topicId, Guid? majorId, string name, string? description, int maxMembers, CancellationToken ct)
    {
        var g = new group
        {
            group_id = Guid.NewGuid(),
            semester_id = semesterId,
            topic_id = topicId,
            major_id = majorId,
            name = name,
            description = description,
            max_members = maxMembers,
            status = "recruiting",
            created_at = DateTime.UtcNow,
            updated_at = DateTime.UtcNow,
        };
        db.groups.Add(g);
        await db.SaveChangesAsync(ct);
        return g.group_id;
    }

    public async Task AddMembershipAsync(Guid groupId, Guid userId, Guid semesterId, string status, CancellationToken ct)
    {
        var m = new group_member
        {
            group_member_id = Guid.NewGuid(),
            group_id = groupId,
            user_id = userId,
            semester_id = semesterId,
            status = status,
            joined_at = DateTime.UtcNow
        };
        db.group_members.Add(m);
        await db.SaveChangesAsync(ct);
    }

    public async Task UpdateMembershipStatusAsync(Guid groupMemberId, string newStatus, CancellationToken ct)
    {
        var m = await db.group_members.FirstOrDefaultAsync(x => x.group_member_id == groupMemberId, ct)
            ?? throw new KeyNotFoundException("Join request not found");
        m.status = newStatus;
        if (newStatus is "member" or "leader")
            m.joined_at = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteMembershipAsync(Guid groupMemberId, CancellationToken ct)
    {
        var m = await db.group_members.FirstOrDefaultAsync(x => x.group_member_id == groupMemberId, ct)
            ?? throw new KeyNotFoundException("Join request not found");
        db.group_members.Remove(m);
        await db.SaveChangesAsync(ct);
    }

    public async Task<bool> LeaveGroupAsync(Guid groupId, Guid userId, CancellationToken ct)
    {
        var m = await db.group_members
            .FirstOrDefaultAsync(x => x.group_id == groupId && x.user_id == userId && (x.status == "member" || x.status == "leader" || x.status == "pending"), ct);
        if (m is null) return false;

        if (m.status == "leader") return false; // leader cannot leave via this endpoint

        if (m.status == "pending")
        {
            db.group_members.Remove(m);
        }
        else
        {
            m.status = "left";
            m.left_at = DateTime.UtcNow;
        }

        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task CloseGroupAsync(Guid groupId, CancellationToken ct)
    {
        // Set group status to 'closed' and mark active memberships as left; remove pendings
        var g = await db.groups.FirstOrDefaultAsync(x => x.group_id == groupId, ct)
            ?? throw new KeyNotFoundException("Group not found");

        g.status = "closed";
        g.updated_at = DateTime.UtcNow;

        var actives = await db.group_members
            .Where(x => x.group_id == groupId && (x.status == "member" || x.status == "leader"))
            .ToListAsync(ct);
        foreach (var m in actives)
        {
            m.status = "left";
            m.left_at = DateTime.UtcNow;
        }

        var pendings = await db.group_members
            .Where(x => x.group_id == groupId && x.status == "pending")
            .ToListAsync(ct);
        if (pendings.Count > 0)
        {
            db.group_members.RemoveRange(pendings);
        }

        await db.SaveChangesAsync(ct);
    }

    public async Task TransferLeadershipAsync(Guid groupId, Guid currentLeaderUserId, Guid newLeaderUserId, CancellationToken ct)
    {
        // Demote current leader -> member, then promote new leader -> leader (ensure unique index)
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var currentLeader = await db.group_members.FirstOrDefaultAsync(x => x.group_id == groupId && x.user_id == currentLeaderUserId && x.status == "leader", ct)
            ?? throw new InvalidOperationException("Current user is not leader of this group");

        var newLeader = await db.group_members.FirstOrDefaultAsync(x => x.group_id == groupId && x.user_id == newLeaderUserId && x.status == "member", ct)
            ?? throw new KeyNotFoundException("New leader must be an existing member of the group");

        currentLeader.status = "member";
        await db.SaveChangesAsync(ct);

        // Now promote new leader
        newLeader.status = "leader";
        await db.SaveChangesAsync(ct);

        await tx.CommitAsync(ct);
    }
}
