using Microsoft.EntityFrameworkCore;
using Teammy.Application.Common.Interfaces.Persistence;
using Teammy.Application.Common.Pagination;
using Teammy.Application.Mentors.ReadModels;
using Teammy.Infrastructure.Persistence;

namespace Teammy.Infrastructure.Repositories;

public sealed class MentorRepository : IMentorRepository
{
    private readonly AppDbContext _db;
    public MentorRepository(AppDbContext db) => _db = db;

    public async Task<PagedResult<OpenGroupReadModel>> ListOpenGroupsAsync(
        Guid termId, Guid? departmentId, string? topic, int page, int size, CancellationToken ct)
    {
        if (page < 1) page = 1;
        if (size is < 1 or > 100) size = 20;

        var q =
            from g in _db.groups.AsNoTracking()
            join tp in _db.topics.AsNoTracking() on g.topic_id equals tp.id
            where g.term_id == termId
            select new { g, tp };

        if (departmentId.HasValue)
        {
            // an toàn nếu cột department_id có tồn tại trên topics
            q = q.Where(x => EF.Property<Guid?>(x.tp, "department_id") == departmentId.Value);
        }

        if (!string.IsNullOrWhiteSpace(topic))
        {
            var t = topic.Trim().ToLower();
            q = q.Where(x =>
                x.tp.title.ToLower().Contains(t) ||
                (x.tp.code != null && x.tp.code.ToLower().Contains(t)));
        }

        var needMentor =
            from x in q
            join tm in _db.topic_mentors.AsNoTracking() on x.tp.id equals tm.topic_id into gj
            from tm in gj.DefaultIfEmpty()
            where tm == null && x.g.status != "closed"
            select new OpenGroupReadModel(
                x.g.id, x.g.name, x.g.status, x.g.capacity,
                x.tp.id, x.tp.title, x.tp.code
            );

        var total = await needMentor.CountAsync(ct);
        var items = await needMentor
            .OrderBy(x => x.Name)
            .Skip((page - 1) * size)
            .Take(size)
            .ToListAsync(ct);

        return new PagedResult<OpenGroupReadModel>(total, page, size, items);
    }

    public async Task<int> CountAssignedTopicsInTermAsync(Guid mentorId, Guid termId, CancellationToken ct)
    {
        var q =
            from tm in _db.topic_mentors.AsNoTracking()
            join tp in _db.topics.AsNoTracking() on tm.topic_id equals tp.id
            where tm.mentor_id == mentorId && tp.term_id == termId
            select tm;
        return await q.CountAsync(ct);
    }

    public Task<bool> ExistsMentorOnTopicAsync(Guid topicId, Guid mentorId, CancellationToken ct)
        => _db.topic_mentors.AsNoTracking()
            .AnyAsync(x => x.topic_id == topicId && x.mentor_id == mentorId, ct);

    public async Task<bool> AddMentorToTopicAsync(Guid topicId, Guid mentorId, string roleOnTopic, CancellationToken ct)
    {
        await _db.topic_mentors.AddAsync(new()
        {
            topic_id = topicId,
            mentor_id = mentorId,
            role_on_topic = roleOnTopic
        }, ct);
        await _db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<(Guid? TopicId, Guid? TermId)> GetGroupTopicAndTermAsync(Guid groupId, CancellationToken ct)
    {
        var g = await _db.groups.AsNoTracking().FirstOrDefaultAsync(x => x.id == groupId, ct);
        return g is null ? (null, null) : (g.topic_id, g.term_id);
    }

    public async Task<bool> RemoveMentorFromGroupAsync(Guid groupId, Guid mentorId, CancellationToken ct)
    {
        var g = await _db.groups.AsNoTracking().FirstOrDefaultAsync(x => x.id == groupId, ct);
        if (g is null || g.topic_id is null) return false;

        var tm = await _db.topic_mentors.FirstOrDefaultAsync(x => x.topic_id == g.topic_id && x.mentor_id == mentorId, ct);
        if (tm is null) return false;

        _db.topic_mentors.Remove(tm);
        await _db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<IReadOnlyList<AssignedGroupReadModel>> GetAssignedGroupsAsync(Guid mentorId, CancellationToken ct)
    {
        var items =
            await (from g in _db.groups.AsNoTracking()
                   join tp in _db.topics.AsNoTracking() on g.topic_id equals tp.id
                   join tm in _db.topic_mentors.AsNoTracking() on tp.id equals tm.topic_id
                   where tm.mentor_id == mentorId
                   orderby g.name
                   select new AssignedGroupReadModel(
                       g.id, g.name, g.status, g.capacity,
                       tp.id, tp.title, tp.code
                   )).ToListAsync(ct);
        return items;
    }

    public async Task<MentorProfileReadModel?> GetMentorProfileAsync(Guid mentorId, CancellationToken ct)
    {
        var u = await _db.users.AsNoTracking().FirstOrDefaultAsync(x => x.id == mentorId, ct);
        if (u is null) return null;

        // TODO: lấy từ user_skills / mentor_profiles nếu có
        return new MentorProfileReadModel(
            u.id, u.display_name, u.email,
            Array.Empty<string>(), null, Array.Empty<object>());
    }
}
