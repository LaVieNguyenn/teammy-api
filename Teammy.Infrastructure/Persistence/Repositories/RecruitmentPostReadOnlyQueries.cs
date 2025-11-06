using Microsoft.EntityFrameworkCore;
using Teammy.Application.Common.Interfaces;
using Teammy.Application.Posts.Dtos;
using Teammy.Infrastructure.Persistence;

namespace Teammy.Infrastructure.Persistence.Repositories;

public sealed class RecruitmentPostReadOnlyQueries(AppDbContext db) : IRecruitmentPostReadOnlyQueries
{
    public Task<Guid?> GetActiveSemesterIdAsync(CancellationToken ct)
        => db.semesters.Where(s => s.is_active).Select(s => (Guid?)s.semester_id).FirstOrDefaultAsync(ct);

    public Task<RecruitmentPostDetailDto?> GetAsync(Guid id, CancellationToken ct)
        => db.recruitment_posts.AsNoTracking()
            .Where(p => p.post_id == id)
            .Select(p => new RecruitmentPostDetailDto(
                p.post_id,
                p.semester_id,
                p.title,
                p.status,
                p.group_id,
                p.major_id,
                p.description,
                p.position_needed,
                p.created_at,
                p.group_id != null
                    ? db.group_members.Count(m => m.group_id == p.group_id && (m.status == "member" || m.status == "leader"))
                    : 0
            ))
            .FirstOrDefaultAsync(ct);

    public async Task<IReadOnlyList<RecruitmentPostSummaryDto>> ListAsync(string? skills, Guid? majorId, string? status, CancellationToken ct)
    {
        var q = db.recruitment_posts.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(status)) q = q.Where(p => p.status == status);
        if (majorId.HasValue) q = q.Where(p => p.major_id == majorId);
        if (!string.IsNullOrWhiteSpace(skills))
        {
            var term = skills.Trim();
            q = q.Where(p => (p.position_needed ?? "").Contains(term) || p.title.Contains(term));
        }

        return await q
            .OrderByDescending(p => p.created_at)
            .Select(p => new RecruitmentPostSummaryDto(
                p.post_id,
                p.semester_id,
                p.title,
                p.status,
                p.group_id,
                p.major_id,
                p.position_needed,
                p.group_id != null
                    ? db.group_members.Count(m => m.group_id == p.group_id && (m.status == "member" || m.status == "leader"))
                    : 0
            ))
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<ApplicationDto>> ListApplicationsAsync(Guid postId, CancellationToken ct)
    {
        var q = from c in db.candidates.AsNoTracking()
                where c.post_id == postId
                join u in db.users.AsNoTracking() on c.applicant_user_id equals u.user_id into uu
                from u in uu.DefaultIfEmpty()
                orderby c.created_at descending
                select new ApplicationDto(
                    c.candidate_id,
                    c.applicant_user_id,
                    c.applicant_group_id,
                    c.status,
                    c.message,
                    c.created_at,
                    u != null ? u.email : null,
                    u != null ? u.display_name : null
                );
        return await q.ToListAsync(ct);
    }

    public Task<(Guid? GroupId, Guid SemesterId, Guid? OwnerUserId)> GetPostOwnerAsync(Guid postId, CancellationToken ct)
        => db.recruitment_posts.AsNoTracking()
            .Where(p => p.post_id == postId)
            .Select(p => new ValueTuple<Guid?, Guid, Guid?>(p.group_id, p.semester_id, p.user_id))
            .FirstOrDefaultAsync(ct);

    public async Task<IReadOnlyList<ProfilePostSummaryDto>> ListProfilePostsAsync(string? skills, Guid? majorId, string? status, CancellationToken ct)
    {
        var q = db.recruitment_posts.AsNoTracking().Where(p => p.post_type == "individual");
        if (!string.IsNullOrWhiteSpace(status)) q = q.Where(p => p.status == status);
        if (majorId.HasValue) q = q.Where(p => p.major_id == majorId);
        if (!string.IsNullOrWhiteSpace(skills))
        {
            var term = skills.Trim();
            q = q.Where(p => (p.position_needed ?? "").Contains(term) || p.title.Contains(term));
        }

        return await q
            .OrderByDescending(p => p.created_at)
            .Select(p => new ProfilePostSummaryDto(
                p.post_id,
                p.semester_id,
                p.title,
                p.status,
                p.user_id,
                p.major_id
            ))
            .ToListAsync(ct);
    }

    public Task<ProfilePostDetailDto?> GetProfilePostAsync(Guid id, CancellationToken ct)
        => db.recruitment_posts.AsNoTracking()
            .Where(p => p.post_id == id && p.post_type == "individual")
            .Select(p => new ProfilePostDetailDto(
                p.post_id,
                p.semester_id,
                p.title,
                p.status,
                p.user_id,
                p.major_id,
                p.description,
                p.created_at
            ))
            .FirstOrDefaultAsync(ct);
}
