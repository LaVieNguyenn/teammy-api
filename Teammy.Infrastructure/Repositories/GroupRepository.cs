using Microsoft.EntityFrameworkCore;
using Teammy.Application.Common.Interfaces.Persistence;
using Teammy.Application.Common.Pagination;
using Teammy.Application.Groups.ReadModels;
using Teammy.Infrastructure.Persistence;

namespace Teammy.Infrastructure.Repositories;

public sealed class GroupRepository : IGroupRepository
{
    private readonly AppDbContext _db;
    public GroupRepository(AppDbContext db) => _db = db;

    public Task<bool> UserHasActiveGroupInTermAsync(Guid userId, Guid termId, CancellationToken ct)
    {
        return _db.group_members.AsNoTracking().AnyAsync(x => x.user_id == userId && x.term_id == termId &&
            (x.status == "pending" || x.status == "member" || x.status == "leader"), ct);
    }

    public async Task<Guid> CreateGroupAsync(Guid termId, string name, int capacity, Guid? topicId, string? description, string? techStack, string? githubUrl, Guid creatorUserId, CancellationToken ct)
    {
        var g = new Infrastructure.Models.group
        {
            term_id = termId,
            name = name,
            capacity = capacity,
            topic_id = topicId,
            status = "recruiting"
        };
        _db.groups.Add(g);
        await _db.SaveChangesAsync(ct);

        _db.group_members.Add(new Infrastructure.Models.group_member
        {
            group_id = g.id,
            user_id = creatorUserId,
            term_id = termId,
            status = "leader"
        });
        await _db.SaveChangesAsync(ct);
        return g.id;
    }

    public async Task<GroupReadModel?> GetByIdAsync(Guid groupId, CancellationToken ct)
    {
        var g = await _db.groups.AsNoTracking().FirstOrDefaultAsync(x => x.id == groupId, ct);
        if (g is null) return null;
        var memberCount = await _db.group_members.AsNoTracking()
            .Where(m => m.group_id == groupId && (m.status == "member" || m.status == "leader"))
            .CountAsync(ct);
        var topic = g.topic_id.HasValue ? await _db.topics.AsNoTracking().FirstOrDefaultAsync(t => t.id == g.topic_id.Value, ct) : null;
        return new GroupReadModel
        {
            Id = g.id,
            TermId = g.term_id,
            TopicId = g.topic_id,
            Name = g.name,
            Capacity = g.capacity,
            Status = g.status,
            CreatedAt = g.created_at,
            Members = memberCount,
            TopicTitle = topic?.title,
            TopicCode = topic?.code
        };
    }

    public async Task<PagedResult<GroupReadModel>> ListOpenAsync(Guid termId, Guid? topicId, Guid? departmentId, Guid? majorId, string? q, int page, int size, CancellationToken ct)
    {
        if (page < 1) page = 1;
        if (size < 1) size = 20; else if (size > 200) size = 200;

        var qbase = from g in _db.groups.AsNoTracking()
                    join tp in _db.topics.AsNoTracking() on g.topic_id equals tp.id into gj
                    from tp in gj.DefaultIfEmpty()
                    where g.term_id == termId && g.status == "recruiting"
                    select new { g, tp };

        if (topicId.HasValue)
            qbase = qbase.Where(x => x.tp != null && x.tp.id == topicId.Value);
        if (departmentId.HasValue)
            qbase = qbase.Where(x => x.tp != null && x.tp.department_id == departmentId.Value);
        if (majorId.HasValue)
            qbase = qbase.Where(x => x.tp != null && x.tp.major_id == majorId.Value);
        if (!string.IsNullOrWhiteSpace(q))
        {
            var s = q.Trim().ToLower();
            qbase = qbase.Where(x => x.g.name.ToLower().Contains(s) || (x.tp != null && (x.tp.title.ToLower().Contains(s) || (x.tp.code != null && x.tp.code.ToLower().Contains(s)))));
        }

        var query = qbase.Select(x => new
        {
            x.g,
            x.tp,
            MemberCount = _db.group_members
                .Count(m => m.group_id == x.g.id && (m.status == "member" || m.status == "leader"))
        })
        .Where(x => x.g.capacity > x.MemberCount);

        var total = await query.CountAsync(ct);
        var items = await query
            .OrderBy(x => x.g.name)
            .Skip((page - 1) * size)
            .Take(size)
            .Select(e => new GroupReadModel
            {
                Id = e.g.id,
                TermId = e.g.term_id,
                TopicId = e.g.topic_id,
                Name = e.g.name,
                Capacity = e.g.capacity,
                Status = e.g.status,
                CreatedAt = e.g.created_at,
                Members = e.MemberCount,
                TopicTitle = e.tp != null ? e.tp.title : null,
                TopicCode = e.tp != null ? e.tp.code : null
            })
            .ToListAsync(ct);

        return new PagedResult<GroupReadModel>(total, page, size, items);
    }

    public async Task<bool> AddJoinRequestAsync(Guid groupId, Guid userId, Guid termId, CancellationToken ct)
    {
        await _db.group_members.AddAsync(new Infrastructure.Models.group_member
        {
            group_id = groupId,
            user_id = userId,
            term_id = termId,
            status = "pending"
        }, ct);
        await _db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<(bool Ok, string? Reason)> LeaveAsync(Guid groupId, Guid userId, CancellationToken ct)
    {
        var member = await _db.group_members.FirstOrDefaultAsync(m => m.group_id == groupId && m.user_id == userId && (m.status == "pending" || m.status == "member" || m.status == "leader"), ct);
        if (member is null) return (false, "NOT_MEMBER");

        if (member.status == "leader")
        {
            var others = await _db.group_members.AsNoTracking().CountAsync(m => m.group_id == groupId && m.user_id != userId && (m.status == "member" || m.status == "leader"), ct);
            if (others > 0) return (false, "LEADER_TRANSFER_REQUIRED");
        }

        member.status = "left";
        member.left_at = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return (true, null);
    }
}
