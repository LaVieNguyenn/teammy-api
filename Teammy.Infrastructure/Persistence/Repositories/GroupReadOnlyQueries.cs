using System.Text.Json;
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

    public async Task<IReadOnlyList<GroupSummaryDto>> ListGroupsAsync(
       string? status, Guid? majorId, Guid? topicId, CancellationToken ct)
    {
        var activeStatuses = new[] { "member", "leader" };

        var q =
            from g in db.groups.AsNoTracking()
            join s in db.semesters.AsNoTracking()
                on g.semester_id equals s.semester_id
            join t in db.topics.AsNoTracking()
                on g.topic_id equals t.topic_id into topicJoin
            from t in topicJoin.DefaultIfEmpty()
            join m in db.majors.AsNoTracking()
                on g.major_id equals m.major_id into majorJoin
            from m in majorJoin.DefaultIfEmpty()
            join u in db.users.AsNoTracking()
                on g.mentor_id equals u.user_id into mentorJoin
            from u in mentorJoin.DefaultIfEmpty()
            select new { g, s, t, m, u};

        if (!string.IsNullOrWhiteSpace(status))
            q = q.Where(x => x.g.status == status);

        if (majorId.HasValue)
            q = q.Where(x => x.g.major_id == majorId.Value);

        if (topicId.HasValue)
            q = q.Where(x => x.g.topic_id == topicId.Value);

        var list = await q
            .Select(x => new GroupSummaryDto(
                x.g.group_id,
                new SemesterDto(
                    x.s.semester_id,
                    x.s.season,
                    x.s.year,
                    x.s.start_date,
                    x.s.end_date,
                    x.s.is_active
                ),
                x.g.name,
                x.g.description,
                x.g.status,
                x.g.max_members,
                x.t == null ? null : new TopicDto(
                    x.t.topic_id,
                    x.t.title,
                    x.t.description
                ),
                x.m == null ? null : new MajorDto(
                    x.m.major_id,
                    x.m.major_name
                ),
                    x.u == null ? null : new MentorDto(
                      x.u.user_id,
                     x.u.display_name,
                      x.u.avatar_url,
                      x.u.email
    ),
                db.group_members.Count(m =>
                    m.group_id == x.g.group_id &&
                    activeStatuses.Contains(m.status)),
                ParseSkills(x.g.skills)
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
                db.group_members.Count(m => m.group_id == g.group_id && activeStatuses.Contains(m.status)),
                ParseSkills(g.skills)
            ))
            .FirstOrDefaultAsync(ct);
    }

    public Task<bool> IsLeaderAsync(Guid groupId, Guid userId, CancellationToken ct)
        => db.group_members.AsNoTracking().AnyAsync(x => x.group_id == groupId && x.user_id == userId && x.status == "leader", ct);

    public Task<Guid?> GetGroupLeaderUserIdAsync(Guid groupId, CancellationToken ct)
        => db.group_members.AsNoTracking()
            .Where(x => x.group_id == groupId && x.status == "leader")
            .OrderByDescending(x => x.joined_at)
            .Select(x => (Guid?)x.user_id)
            .FirstOrDefaultAsync(ct);

    public Task<bool> HasActiveMembershipInSemesterAsync(Guid userId, Guid semesterId, CancellationToken ct)
        => db.group_members.AsNoTracking().AnyAsync(x => x.user_id == userId && x.semester_id == semesterId && (x.status == "pending" || x.status == "member" || x.status == "leader"), ct);

    public Task<bool> HasActiveGroupAsync(Guid userId, Guid semesterId, CancellationToken ct)
        => db.group_members.AsNoTracking()
            .Join(db.groups.AsNoTracking(),
                m => m.group_id,
                g => g.group_id,
                (m, g) => new { m, g })
            .AnyAsync(x => x.m.user_id == userId
                           && x.m.semester_id == semesterId
                           && (x.m.status == "member" || x.m.status == "leader")
                           && x.g.status == "active", ct);

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

    public async Task<IReadOnlyList<MyGroupDto>> ListMyGroupsAsync(
        Guid userId,
        Guid? semesterId,
        CancellationToken ct)
    {
        Guid? semId = semesterId;
        if (!semId.HasValue)
        {
            semId = await db.semesters.AsNoTracking()
                .Where(s => s.is_active)
                .Select(s => (Guid?)s.semester_id)
                .FirstOrDefaultAsync(ct);
        }

        if (!semId.HasValue)
            return Array.Empty<MyGroupDto>();

        var activeStatuses = new[] { "member", "leader" };
        var memberGroup = await (
            from m in db.group_members.AsNoTracking()
            join g in db.groups.AsNoTracking() on m.group_id equals g.group_id
            where m.user_id == userId && g.semester_id == semId.Value
            select new MyGroupDto(
                g.group_id,
                g.semester_id,
                g.name,
                g.status,
                g.max_members,
                db.group_members.Count(x => x.group_id == g.group_id && activeStatuses.Contains(x.status)),
                m.status
            )
        ).FirstOrDefaultAsync(ct);

        if (memberGroup is not null)
            return new[] { memberGroup };
        var mentorGroup = await (
            from g in db.groups.AsNoTracking()
            where g.mentor_id == userId && g.semester_id == semId.Value
            select new MyGroupDto(
                g.group_id,
                g.semester_id,
                g.name,
                g.status,
                g.max_members,
                db.group_members.Count(x => x.group_id == g.group_id && activeStatuses.Contains(x.status)),
                "mentor"
            )
        ).FirstOrDefaultAsync(ct);

        if (mentorGroup is not null)
            return new[] { mentorGroup };
        return Array.Empty<MyGroupDto>();
    }
    public async Task<GroupMentorDto?> GetMentorAsync(Guid groupId, CancellationToken ct)
    {
        return await (from g in db.groups.AsNoTracking()
                      join u in db.users.AsNoTracking() on g.mentor_id equals u.user_id
                      where g.group_id == groupId && g.mentor_id != null
                      select new GroupMentorDto(
                          u.user_id,
                          u.email!,
                          u.display_name!,
                          u.avatar_url))
            .FirstOrDefaultAsync(ct);
    }
    public async Task<IReadOnlyList<Teammy.Application.Groups.Dtos.GroupMemberDto>> ListActiveMembersAsync(Guid groupId, CancellationToken ct)
    {
        var members = await (from m in db.group_members.AsNoTracking()
                             join u in db.users.AsNoTracking() on m.user_id equals u.user_id
                             where m.group_id == groupId && (m.status == "member" || m.status == "leader")
                             orderby m.status descending, m.joined_at
                             select new { Member = m, User = u }).ToListAsync(ct);

        var memberIds = members.Select(x => x.Member.group_member_id).ToList();
        var rolesLookup = await db.group_member_roles.AsNoTracking()
            .Where(r => memberIds.Contains(r.group_member_id))
            .GroupBy(r => r.group_member_id)
            .Select(g => new
            {
                GroupMemberId = g.Key,
                Role = g
                    .OrderByDescending(r => r.assigned_at)
                    .Select(r => r.role_name)
                    .FirstOrDefault()
            })
            .ToDictionaryAsync(x => x.GroupMemberId, x => x.Role, ct);

        return members.Select(x => new Teammy.Application.Groups.Dtos.GroupMemberDto(
            x.User.user_id,
            x.User.email!,
            x.User.display_name!,
            x.Member.status,
            x.Member.joined_at,
            x.User.avatar_url,
            rolesLookup.TryGetValue(x.Member.group_member_id, out var role) ? role : null)).ToList();
    }

    public Task<bool> IsActiveMemberAsync(Guid groupId, Guid userId, CancellationToken ct)
        => db.group_members.AsNoTracking()
            .AnyAsync(m => m.group_id == groupId
                           && m.user_id == userId
                           && (m.status == "member" || m.status == "leader"), ct);
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
    public Task<(Guid TopicId, string Title,string? Description, string Status, Guid CreatedBy, DateTime? CreatedAt)?> GetTopicAsync(Guid topicId, CancellationToken ct)
        => db.topics.AsNoTracking()
            .Where(t => t.topic_id == topicId)
            .Select(t => new ValueTuple<Guid, string, string?, string, Guid, DateTime?>(t.topic_id, t.title, t.description, t.status, t.created_by, t.created_at))
            .FirstOrDefaultAsync(ct)
            .ContinueWith(t => t.Result == default ? (ValueTuple<Guid, string, string?, string, Guid, DateTime?>?)null : t.Result, ct);

    public async Task<(int MinSize, int MaxSize)> GetGroupSizePolicyAsync(Guid semesterId, CancellationToken ct)
    {
        var policy = await db.semester_policies.AsNoTracking()
            .Where(p => p.semester_id == semesterId)
            .Select(p => new { p.desired_group_size_min, p.desired_group_size_max })
            .FirstOrDefaultAsync(ct);
        if (policy is null)
            return (4, 6);
        var min = policy.desired_group_size_min <= 0 ? 4 : policy.desired_group_size_min;
        var max = policy.desired_group_size_max < min ? min : policy.desired_group_size_max;
        return (min, max);
    }
    public Task<(Guid MajorId, string MajorName)?> GetMajorAsync(Guid majorId, CancellationToken ct)
        => db.majors.AsNoTracking()
            .Where(m => m.major_id == majorId)
            .Select(m => new ValueTuple<Guid, string>(m.major_id, m.major_name))
            .FirstOrDefaultAsync(ct)
            .ContinueWith(t => t.Result == default ? (ValueTuple<Guid, string>?)null : t.Result, ct);

    public Task<bool> GroupNameExistsAsync(Guid semesterId, string name, Guid? excludeGroupId, CancellationToken ct)
        => db.groups.AsNoTracking()
            .AnyAsync(g => g.semester_id == semesterId && g.name == name && (!excludeGroupId.HasValue || g.group_id != excludeGroupId.Value), ct);

    public async Task<IReadOnlyList<Teammy.Application.Groups.Dtos.GroupPendingItemDto>> GetUnifiedPendingAsync(Guid groupId, CancellationToken ct)
    {
        var apps = await (
            from c in db.candidates.AsNoTracking()
            join p in db.recruitment_posts.AsNoTracking() on c.post_id equals p.post_id
            join u in db.users.AsNoTracking() on c.applicant_user_id equals u.user_id
            where p.group_id == groupId && c.status == "pending" && c.applicant_user_id != null
            orderby c.created_at descending
            select new Teammy.Application.Groups.Dtos.GroupPendingItemDto(
                "application",
                c.candidate_id,
                p.post_id,
                u.user_id,
                u.email!,
                u.display_name!,
                u.avatar_url,
                c.created_at,
                c.message,
                null,
                null,
                null)
        ).ToListAsync(ct);

        var invs = await (
            from i in db.invitations.AsNoTracking()
            join u in db.users.AsNoTracking() on i.invitee_user_id equals u.user_id into uu
            from u in uu.DefaultIfEmpty()
            join t in db.topics.AsNoTracking() on i.topic_id equals t.topic_id into tt
            from t in tt.DefaultIfEmpty()
            where i.group_id == groupId && i.status == "pending"
            orderby i.created_at descending
            select new Teammy.Application.Groups.Dtos.GroupPendingItemDto(
                i.topic_id != null ? "mentor_invitation" : "invitation",
                i.invitation_id,
                null,
                i.invitee_user_id,
                u != null ? u.email! : string.Empty,
                u != null ? u.display_name! : string.Empty,
                u != null ? u.avatar_url : null,
                i.created_at,
                i.message,
                i.topic_id,
                t != null ? t.title : null,
                i.responded_at)
        ).ToListAsync(ct);

        var combined = apps
            .Concat(invs)
            .ToList();

        var reminder = await CreateMentorReminderAsync(groupId, ct);
        if (reminder is not null)
            combined.Add(reminder);

        return combined
            .OrderByDescending(x => x.CreatedAt)
            .ToList();
    }

    private static IReadOnlyList<string>? ParseSkills(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var tokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            CollectSkillTokens(doc.RootElement, tokens);
            if (tokens.Count == 0) return null;
            return tokens
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch
        {
            return null;
        }
    }

    private static void CollectSkillTokens(JsonElement element, HashSet<string> tokens)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Array:
                foreach (var el in element.EnumerateArray())
                    CollectSkillTokens(el, tokens);
                break;
            case JsonValueKind.String:
                foreach (var token in Tokenize(el: element.GetString()))
                    tokens.Add(token);
                break;
            case JsonValueKind.Object:
                if (element.TryGetProperty("skill_tags", out var skillTags))
                    CollectSkillTokens(skillTags, tokens);
                if (element.TryGetProperty("skills", out var skills))
                    CollectSkillTokens(skills, tokens);
                if (element.TryGetProperty("primary_role", out var primary))
                    CollectSkillTokens(primary, tokens);
                break;
        }
    }

    private static IEnumerable<string> Tokenize(string? el)
    {
        if (string.IsNullOrWhiteSpace(el))
            yield break;
        var separators = new[] { ',', ';', '/', '|', '\n', '\r' };
        foreach (var part in el.Split(separators, StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = part.Trim();
            if (!string.IsNullOrWhiteSpace(trimmed))
                yield return trimmed;
        }
    }

    private async Task<GroupPendingItemDto?> CreateMentorReminderAsync(Guid groupId, CancellationToken ct)
    {
        var info = await db.groups.AsNoTracking()
            .Where(g => g.group_id == groupId)
            .Select(g => new
            {
                g.group_id,
                g.topic_id,
                g.mentor_id,
                g.max_members,
                g.updated_at
            })
            .FirstOrDefaultAsync(ct);

        if (info is null || !info.topic_id.HasValue || !info.mentor_id.HasValue)
            return null;

        var activeStatuses = new[] { "member", "leader" };
        var activeCount = await db.group_members.AsNoTracking()
            .CountAsync(m => m.group_id == groupId && activeStatuses.Contains(m.status), ct);
        if (activeCount >= info.max_members)
            return null;

        var topicTitle = await db.topics.AsNoTracking()
            .Where(t => t.topic_id == info.topic_id.Value)
            .Select(t => t.title)
            .FirstOrDefaultAsync(ct);

        var missing = info.max_members - activeCount;
        var message = $"Group mentor confirmed but only {activeCount}/{info.max_members} members. Recruit {missing} more member{(missing == 1 ? string.Empty : "s")}.";

        return new GroupPendingItemDto(
            "member_reminder",
            groupId,
            null,
            Guid.Empty,
            string.Empty,
            string.Empty,
            null,
            info.updated_at,
            message,
            info.topic_id,
            topicTitle,
            null);
    }
}
