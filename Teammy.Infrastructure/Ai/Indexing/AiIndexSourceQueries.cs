using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Teammy.Application.Common.Interfaces;
using Teammy.Infrastructure.Persistence;

namespace Teammy.Infrastructure.Ai.Indexing;

public sealed class AiIndexSourceQueries : IAiIndexSourceQueries
{
    private readonly AppDbContext _db;

    public AiIndexSourceQueries(AppDbContext db)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
    }

    public async Task<TopicIndexRow?> GetTopicAsync(Guid topicId, CancellationToken ct)
    {
        var row = await _db.topics
            .AsNoTracking()
            .Where(t => t.topic_id == topicId)
            .Select(t => new
            {
                t.topic_id,
                t.semester_id,
                t.major_id,
                t.title,
                t.description,
                t.skills
            })
            .FirstOrDefaultAsync(ct);

        if (row is null)
            return null;

        return new TopicIndexRow(
            row.topic_id,
            row.semester_id,
            row.major_id,
            row.title,
            row.description,
            ParseSkillArray(row.skills),
            row.skills);
    }

    public async Task<RecruitmentPostIndexRow?> GetRecruitmentPostAsync(Guid postId, CancellationToken ct)
    {
        var row = await (
                from post in _db.recruitment_posts.AsNoTracking()
                where post.post_id == postId && post.post_type == "group_hiring"
                join major in _db.majors.AsNoTracking() on post.major_id equals major.major_id into majorJoin
                from major in majorJoin.DefaultIfEmpty()
                join grp in _db.groups.AsNoTracking() on post.group_id equals grp.group_id into groupJoin
                from grp in groupJoin.DefaultIfEmpty()
                select new
                {
                    post.post_id,
                    post.semester_id,
                    post.major_id,
                    post.title,
                    post.description,
                    MajorName = major != null ? major.major_name : null,
                    GroupName = grp != null ? grp.name : null,
                    post.position_needed,
                    post.required_skills
                })
            .FirstOrDefaultAsync(ct);

        if (row is null)
            return null;

        return new RecruitmentPostIndexRow(
            row.post_id,
            row.semester_id,
            row.major_id,
            row.title,
            row.description,
            row.MajorName,
            row.GroupName,
            row.position_needed,
            row.required_skills);
    }

    public async Task<ProfilePostIndexRow?> GetProfilePostAsync(Guid postId, CancellationToken ct)
    {
        var row = await (
                from post in _db.recruitment_posts.AsNoTracking()
                where post.post_id == postId && post.post_type == "individual"
                join owner in _db.users.AsNoTracking() on post.user_id equals owner.user_id into ownerJoin
                from owner in ownerJoin.DefaultIfEmpty()
                join pool in _db.mv_students_pools.AsNoTracking()
                    on new { user_id = post.user_id, semester_id = (Guid?)post.semester_id }
                    equals new { user_id = pool.user_id, semester_id = pool.semester_id } into poolJoin
                from pool in poolJoin.DefaultIfEmpty()
                select new
                {
                    post.post_id,
                    post.semester_id,
                    post.major_id,
                    post.title,
                    post.description,
                    OwnerDisplayName = owner.display_name ?? owner.email,
                    SkillsJson = pool.skills ?? owner.skills,
                    SkillsText = post.position_needed,
                    PrimaryRole = pool.primary_role
                })
            .FirstOrDefaultAsync(ct);

        if (row is null)
            return null;

        return new ProfilePostIndexRow(
            row.post_id,
            row.semester_id,
            row.major_id,
            row.title,
            row.description,
            row.OwnerDisplayName,
            row.SkillsJson,
            row.SkillsText,
            row.PrimaryRole);
    }

    private static IReadOnlyList<string> ParseSkillArray(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return Array.Empty<string>();

        try
        {
            var list = JsonSerializer.Deserialize<List<string>>(json);
            return list is null
                ? Array.Empty<string>()
                : list
                    .Select(x => x?.Trim())
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Select(x => x!)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }
}
