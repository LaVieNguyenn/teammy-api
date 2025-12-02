using Microsoft.EntityFrameworkCore;
using Teammy.Application.Common.Interfaces;
using Teammy.Application.Groups.Dtos;
using Teammy.Infrastructure.Persistence;
using Teammy.Infrastructure.Persistence.Models;

namespace Teammy.Infrastructure.Persistence.Repositories;

public sealed class GroupRepository(AppDbContext db) : IGroupRepository
{
    private Task<group_member?> FindGroupMemberAsync(Guid groupId, Guid userId, CancellationToken ct)
        => db.group_members.FirstOrDefaultAsync(x => x.group_id == groupId && x.user_id == userId, ct);

    public async Task<Guid> CreateGroupAsync(Guid semesterId, Guid? topicId, Guid? majorId, string name, string? description, int maxMembers, string? skillsJson, CancellationToken ct)
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
            skills = skillsJson
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
        // Do not filter by status; allow leaving regardless of current status value
        var m = await db.group_members
            .FirstOrDefaultAsync(x => x.group_id == groupId && x.user_id == userId, ct);
        if (m is null) return false;
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        db.group_members.Remove(m);
        await db.SaveChangesAsync(ct);

        // Cleanup user-related applications and invitations for this group's posts
        var postIds = await db.recruitment_posts.AsNoTracking()
            .Where(p => p.group_id == groupId)
            .Select(p => p.post_id)
            .ToListAsync(ct);
        if (postIds.Count > 0)
        {
            var userCands = await db.candidates
                .Where(c => postIds.Contains(c.post_id) && (c.applicant_user_id == userId || c.applied_by_user_id == userId))
                .ToListAsync(ct);
            if (userCands.Count > 0)
            {
                db.candidates.RemoveRange(userCands);
                await db.SaveChangesAsync(ct);
            }
        }

        // Invitations are now group-based; clean by group_id
        var userInvs = await db.invitations
            .Where(i => i.group_id == groupId && i.invitee_user_id == userId)
            .ToListAsync(ct);
        if (userInvs.Any())
        {
            db.invitations.RemoveRange(userInvs);
            await db.SaveChangesAsync(ct);
        }

        var remaining = await db.group_members.AsNoTracking().CountAsync(x => x.group_id == groupId, ct);
        if (remaining == 0)
        {
            // Remove group's recruitment posts first (defensive, though FK is Cascade)
            var posts = await db.recruitment_posts.Where(p => p.group_id == groupId).ToListAsync(ct);
            if (posts.Count > 0)
            {
                db.recruitment_posts.RemoveRange(posts);
                await db.SaveChangesAsync(ct);
            }

            var g = await db.groups.FirstOrDefaultAsync(x => x.group_id == groupId, ct);
            if (g != null)
            {
                db.groups.Remove(g);
                await db.SaveChangesAsync(ct);
            }
        }

        await tx.CommitAsync(ct);
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

    public async Task UpdateGroupAsync(Guid groupId, string? name, string? description, int? maxMembers, Guid? majorId, Guid? topicId, Guid? mentorId, string? skillsJson, CancellationToken ct)
    {
        var g = await db.groups.FirstOrDefaultAsync(x => x.group_id == groupId, ct)
            ?? throw new KeyNotFoundException("Group not found");

        if (name is not null) g.name = name;
        if (description is not null) g.description = description;
        if (maxMembers.HasValue) g.max_members = maxMembers.Value;
        if (majorId.HasValue) g.major_id = majorId;
        if (topicId.HasValue) g.topic_id = topicId;
        if (mentorId.HasValue) g.mentor_id = mentorId;
        if (skillsJson is not null) g.skills = skillsJson;
        g.updated_at = DateTime.UtcNow;

        await db.SaveChangesAsync(ct);
    }

    public async Task SetStatusAsync(Guid groupId, string newStatus, CancellationToken ct)
    {
        var g = await db.groups.FirstOrDefaultAsync(x => x.group_id == groupId, ct)
            ?? throw new KeyNotFoundException("Group not found");
        g.status = newStatus;
        g.updated_at = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<GroupMemberRoleDto>> ListMemberRolesAsync(Guid groupId, Guid memberUserId, CancellationToken ct)
    {
        var member = await FindGroupMemberAsync(groupId, memberUserId, ct)
            ?? throw new KeyNotFoundException("Member not found in group");

        return await (
            from r in db.group_member_roles.AsNoTracking()
            join u in db.users.AsNoTracking() on r.assigned_by equals u.user_id into assignedByJoin
            from assignedBy in assignedByJoin.DefaultIfEmpty()
            join gm in db.group_members.AsNoTracking() on r.group_member_id equals gm.group_member_id
            join memberUser in db.users.AsNoTracking() on gm.user_id equals memberUser.user_id
            where r.group_member_id == member.group_member_id
            orderby r.role_name
            select new GroupMemberRoleDto(
                r.group_member_role_id,
                r.group_member_id,
                memberUser.user_id,
                memberUser.display_name ?? string.Empty,
                r.role_name,
                r.assigned_by,
                assignedBy != null ? assignedBy.display_name : null,
                r.assigned_at)).ToListAsync(ct);
    }

    public async Task AddMemberRoleAsync(Guid groupId, Guid memberUserId, Guid assignedByUserId, string roleName, CancellationToken ct)
    {
        var name = NormalizeRole(roleName);
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("roleName is required");

        var member = await FindGroupMemberAsync(groupId, memberUserId, ct)
            ?? throw new KeyNotFoundException("Member not found in group");

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var existing = await db.group_member_roles
            .Where(r => r.group_member_id == member.group_member_id)
            .ToListAsync(ct);
        if (existing.Count > 0)
            db.group_member_roles.RemoveRange(existing);

        db.group_member_roles.Add(new group_member_role
        {
            group_member_role_id = Guid.NewGuid(),
            group_member_id = member.group_member_id,
            role_name = name,
            assigned_by = assignedByUserId,
            assigned_at = DateTime.UtcNow
        });

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    public async Task RemoveMemberRoleAsync(Guid groupId, Guid memberUserId, string roleName, CancellationToken ct)
    {
        var name = NormalizeRole(roleName);
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("roleName is required");

        var member = await FindGroupMemberAsync(groupId, memberUserId, ct)
            ?? throw new KeyNotFoundException("Member not found in group");

        var role = await db.group_member_roles
            .FirstOrDefaultAsync(r => r.group_member_id == member.group_member_id && r.role_name == name, ct);

        if (role is null)
            throw new KeyNotFoundException("Role not found for member");

        db.group_member_roles.Remove(role);
        await db.SaveChangesAsync(ct);
    }

    public async Task ReplaceMemberRolesAsync(Guid groupId, Guid memberUserId, Guid assignedByUserId, IReadOnlyCollection<string> roleNames, CancellationToken ct)
    {
        var member = await FindGroupMemberAsync(groupId, memberUserId, ct)
            ?? throw new KeyNotFoundException("Member not found in group");

        var normalized = roleNames?
            .Select(NormalizeRole)
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList() ?? new List<string>();

        if (normalized.Count > 1)
            throw new ArgumentException("Only one role may be assigned to a member.");

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var existing = await db.group_member_roles
            .Where(r => r.group_member_id == member.group_member_id)
            .ToListAsync(ct);

        if (existing.Count > 0)
            db.group_member_roles.RemoveRange(existing);

        if (normalized.Count == 1)
        {
            db.group_member_roles.Add(new group_member_role
            {
                group_member_role_id = Guid.NewGuid(),
                group_member_id = member.group_member_id,
                role_name = normalized[0],
                assigned_by = assignedByUserId,
                assigned_at = DateTime.UtcNow
            });
        }

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    private static string NormalizeRole(string roleName)
        => roleName?.Trim() ?? string.Empty;
}
