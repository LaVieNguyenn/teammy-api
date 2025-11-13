using Microsoft.EntityFrameworkCore;
using Teammy.Application.Common.Interfaces;
using Teammy.Application.Posts.Dtos;
using Teammy.Infrastructure.Persistence;

namespace Teammy.Infrastructure.Persistence.Repositories;

public sealed class RecruitmentPostReadOnlyQueries(AppDbContext db) : IRecruitmentPostReadOnlyQueries
{
    public Task<Guid?> GetActiveSemesterIdAsync(CancellationToken ct)
        => db.semesters.Where(s => s.is_active).Select(s => (Guid?)s.semester_id).FirstOrDefaultAsync(ct);

    public Task<RecruitmentPostDetailDto?> GetAsync(Guid id, Teammy.Application.Posts.Dtos.ExpandOptions expand, Guid? currentUserId, CancellationToken ct)
        => db.recruitment_posts.AsNoTracking()
            .Where(p => p.post_id == id)
            .Join(db.semesters.AsNoTracking(), p => p.semester_id, s => s.semester_id, (p, s) => new { p, s })
            .GroupJoin(db.groups.AsNoTracking(), ps => ps.p.group_id, g => g.group_id, (ps, grps) => new { ps, grps })
            .SelectMany(x => x.grps.DefaultIfEmpty(), (x, g) => new { x.ps.p, x.ps.s, g })
            .GroupJoin(db.majors.AsNoTracking(), t => t.p.major_id, m => m.major_id, (t, ms) => new { t.p, t.s, t.g, ms })
            .SelectMany(x => x.ms.DefaultIfEmpty(), (x, m) => new { x.p, x.s, x.g, m })
            .Select(x => new RecruitmentPostDetailDto(
                x.p.post_id,
                x.p.semester_id,
                string.Concat(x.s.season ?? "", " ", (x.s.year.HasValue ? x.s.year.Value.ToString() : "")),
                (expand & Teammy.Application.Posts.Dtos.ExpandOptions.Semester) != 0
                    ? new PostSemesterDto(x.s.semester_id, x.s.season, x.s.year, x.s.start_date, x.s.end_date, x.s.is_active)
                    : null,
                x.p.title,
                x.p.status,
                x.p.post_type,
                x.p.group_id,
                x.g != null ? x.g.name : null,
                (expand & Teammy.Application.Posts.Dtos.ExpandOptions.Group) != 0 && x.g != null
                    ? new PostGroupDto(x.g.group_id, x.g.name, x.g.description, x.g.status, x.g.max_members, x.g.major_id, x.g.topic_id)
                    : null,
                x.p.major_id,
                x.m != null ? x.m.major_name : null,
                (expand & Teammy.Application.Posts.Dtos.ExpandOptions.Major) != 0 && x.m != null
                    ? new PostMajorDto(x.m.major_id, x.m.major_name)
                    : null,
                x.p.description,
                x.p.position_needed,
                x.p.created_at,
                x.p.group_id != null
                    ? db.group_members.Count(mb => mb.group_id == x.p.group_id && (mb.status == "member" || mb.status == "leader"))
                    : 0,
                x.p.application_deadline,
                currentUserId.HasValue && db.candidates.Any(c => c.post_id == x.p.post_id && c.applicant_user_id == currentUserId.Value),
                currentUserId.HasValue
                    ? db.candidates.Where(c => c.post_id == x.p.post_id && c.applicant_user_id == currentUserId.Value)
                        .Select(c => (Guid?)c.candidate_id)
                        .FirstOrDefault()
                    : null,
                currentUserId.HasValue
                    ? db.candidates.Where(c => c.post_id == x.p.post_id && c.applicant_user_id == currentUserId.Value)
                        .Select(c => c.status)
                        .FirstOrDefault()
                    : null,
                db.candidates.Count(c => c.post_id == x.p.post_id)
            ))
            .FirstOrDefaultAsync(ct);

    public async Task<IReadOnlyList<RecruitmentPostSummaryDto>> ListAsync(string? skills, Guid? majorId, string? status, Teammy.Application.Posts.Dtos.ExpandOptions expand, Guid? currentUserId, CancellationToken ct)
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
            .Join(db.semesters.AsNoTracking(), p => p.semester_id, s => s.semester_id, (p, s) => new { p, s })
            .GroupJoin(db.groups.AsNoTracking(), ps => ps.p.group_id, g => g.group_id, (ps, grps) => new { ps, grps })
            .SelectMany(x => x.grps.DefaultIfEmpty(), (x, g) => new { x.ps.p, x.ps.s, g })
            .GroupJoin(db.majors.AsNoTracking(), t => t.p.major_id, m => m.major_id, (t, ms) => new { t.p, t.s, t.g, ms })
            .SelectMany(x => x.ms.DefaultIfEmpty(), (x, m) => new { x.p, x.s, x.g, m })
            .Select(x => new RecruitmentPostSummaryDto(
                x.p.post_id,
                x.p.semester_id,
                string.Concat(x.s.season ?? "", " ", (x.s.year.HasValue ? x.s.year.Value.ToString() : "")),
                (expand & Teammy.Application.Posts.Dtos.ExpandOptions.Semester) != 0
                    ? new PostSemesterDto(x.s.semester_id, x.s.season, x.s.year, x.s.start_date, x.s.end_date, x.s.is_active)
                    : null,
                x.p.title,
                x.p.status,
                x.p.post_type,
                x.p.group_id,
                x.g != null ? x.g.name : null,
                (expand & Teammy.Application.Posts.Dtos.ExpandOptions.Group) != 0 && x.g != null
                    ? new PostGroupDto(x.g.group_id, x.g.name, x.g.description, x.g.status, x.g.max_members, x.g.major_id, x.g.topic_id)
                    : null,
                x.p.major_id,
                x.m != null ? x.m.major_name : null,
                (expand & Teammy.Application.Posts.Dtos.ExpandOptions.Major) != 0 && x.m != null
                    ? new PostMajorDto(x.m.major_id, x.m.major_name)
                    : null,
                x.p.position_needed,
                x.p.group_id != null
                    ? db.group_members.Count(mb => mb.group_id == x.p.group_id && (mb.status == "member" || mb.status == "leader"))
                    : 0,
                x.p.description,
                x.p.created_at,
                x.p.application_deadline,
                currentUserId.HasValue && db.candidates.Any(c => c.post_id == x.p.post_id && c.applicant_user_id == currentUserId.Value),
                currentUserId.HasValue
                    ? db.candidates.Where(c => c.post_id == x.p.post_id && c.applicant_user_id == currentUserId.Value)
                        .Select(c => (Guid?)c.candidate_id)
                        .FirstOrDefault()
                    : null,
                currentUserId.HasValue
                    ? db.candidates.Where(c => c.post_id == x.p.post_id && c.applicant_user_id == currentUserId.Value)
                        .Select(c => c.status)
                        .FirstOrDefault()
                    : null,
                db.candidates.Count(c => c.post_id == x.p.post_id)
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

    public Task<(Guid ApplicationId, Guid PostId)?> FindPendingApplicationInGroupAsync(Guid groupId, Guid userId, CancellationToken ct)
        => (from c in db.candidates.AsNoTracking()
            join p in db.recruitment_posts.AsNoTracking() on c.post_id equals p.post_id
            where p.group_id == groupId && c.applicant_user_id == userId && c.status == "pending"
            orderby c.created_at descending
            select new ValueTuple<Guid, Guid>(c.candidate_id, c.post_id))
            .FirstOrDefaultAsync(ct)
            .ContinueWith(t => t.Result == default ? (ValueTuple<Guid,Guid>?)null : t.Result, ct);

    public Task<(Guid ApplicationId, string Status)?> FindApplicationByPostAndUserAsync(Guid postId, Guid userId, CancellationToken ct)
        => db.candidates.AsNoTracking()
            .Where(c => c.post_id == postId && c.applicant_user_id == userId)
            .OrderByDescending(c => c.created_at)
            .Select(c => new ValueTuple<Guid, string>(c.candidate_id, c.status))
            .FirstOrDefaultAsync(ct)
            .ContinueWith(t => t.Result == default ? (ValueTuple<Guid,string>?)null : t.Result, ct);

    public async Task<IReadOnlyList<RecruitmentPostSummaryDto>> ListAppliedByUserAsync(Guid userId, Teammy.Application.Posts.Dtos.ExpandOptions expand, CancellationToken ct)
    {
        var q = db.recruitment_posts.AsNoTracking()
            .Where(p => db.candidates.Any(c => c.post_id == p.post_id && c.applicant_user_id == userId));

        return await q
            .OrderByDescending(p => p.created_at)
            .Join(db.semesters.AsNoTracking(), p => p.semester_id, s => s.semester_id, (p, s) => new { p, s })
            .GroupJoin(db.groups.AsNoTracking(), ps => ps.p.group_id, g => g.group_id, (ps, grps) => new { ps, grps })
            .SelectMany(x => x.grps.DefaultIfEmpty(), (x, g) => new { x.ps.p, x.ps.s, g })
            .GroupJoin(db.majors.AsNoTracking(), t => t.p.major_id, m => m.major_id, (t, ms) => new { t.p, t.s, t.g, ms })
            .SelectMany(x => x.ms.DefaultIfEmpty(), (x, m) => new { x.p, x.s, x.g, m })
            .Select(x => new RecruitmentPostSummaryDto(
                x.p.post_id,
                x.p.semester_id,
                string.Concat(x.s.season ?? "", " ", (x.s.year.HasValue ? x.s.year.Value.ToString() : "")),
                (expand & Teammy.Application.Posts.Dtos.ExpandOptions.Semester) != 0
                    ? new PostSemesterDto(x.s.semester_id, x.s.season, x.s.year, x.s.start_date, x.s.end_date, x.s.is_active)
                    : null,
                x.p.title,
                x.p.status,
                x.p.post_type,
                x.p.group_id,
                x.g != null ? x.g.name : null,
                (expand & Teammy.Application.Posts.Dtos.ExpandOptions.Group) != 0 && x.g != null
                    ? new PostGroupDto(x.g.group_id, x.g.name, x.g.description, x.g.status, x.g.max_members, x.g.major_id, x.g.topic_id)
                    : null,
                x.p.major_id,
                x.m != null ? x.m.major_name : null,
                (expand & Teammy.Application.Posts.Dtos.ExpandOptions.Major) != 0 && x.m != null
                    ? new PostMajorDto(x.m.major_id, x.m.major_name)
                    : null,
                x.p.position_needed,
                x.p.group_id != null
                    ? db.group_members.Count(mb => mb.group_id == x.p.group_id && (mb.status == "member" || mb.status == "leader"))
                    : 0,
                x.p.description,
                x.p.created_at,
                x.p.application_deadline,
                true, // hasApplied (by construction)
                db.candidates.Where(c => c.post_id == x.p.post_id && c.applicant_user_id == userId)
                    .Select(c => (Guid?)c.candidate_id).FirstOrDefault(),
                db.candidates.Where(c => c.post_id == x.p.post_id && c.applicant_user_id == userId)
                    .Select(c => c.status).FirstOrDefault(),
                db.candidates.Count(c => c.post_id == x.p.post_id)
            ))
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<ProfilePostSummaryDto>> ListProfilePostsAsync(string? skills, Guid? majorId, string? status, Teammy.Application.Posts.Dtos.ExpandOptions expand, CancellationToken ct)
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
            .Join(db.semesters.AsNoTracking(), p => p.semester_id, s => s.semester_id, (p, s) => new { p, s })
            .GroupJoin(db.majors.AsNoTracking(), ps => ps.p.major_id, m => m.major_id, (ps, ms) => new { ps, ms })
            .SelectMany(x => x.ms.DefaultIfEmpty(), (x, m) => new { x.ps.p, x.ps.s, m })
            .GroupJoin(db.users.AsNoTracking(), t => t.p.user_id, u => u.user_id, (t, us) => new { t.p, t.s, t.m, us })
            .SelectMany(x => x.us.DefaultIfEmpty(), (x, u) => new { x.p, x.s, x.m, u })
            .Select(x => new ProfilePostSummaryDto(
                x.p.post_id,
                x.p.semester_id,
                string.Concat(x.s.season ?? "", " ", (x.s.year.HasValue ? x.s.year.Value.ToString() : "")),
                (expand & Teammy.Application.Posts.Dtos.ExpandOptions.Semester) != 0
                    ? new PostSemesterDto(x.s.semester_id, x.s.season, x.s.year, x.s.start_date, x.s.end_date, x.s.is_active)
                    : null,
                x.p.title,
                x.p.status,
                x.p.post_type,
                x.p.user_id,
                x.u != null ? x.u.display_name : null,
                (expand & Teammy.Application.Posts.Dtos.ExpandOptions.User) != 0 && x.u != null
                    ? new PostUserDto(x.u.user_id, x.u.email, x.u.display_name, x.u.avatar_url, x.u.email_verified)
                    : null,
                x.p.major_id,
                x.m != null ? x.m.major_name : null,
                (expand & Teammy.Application.Posts.Dtos.ExpandOptions.Major) != 0 && x.m != null
                    ? new PostMajorDto(x.m.major_id, x.m.major_name)
                    : null,
                x.p.description,
                x.p.position_needed, // Skills (for individual posts)
                x.p.created_at
            ))
            .ToListAsync(ct);
    }

    public Task<ProfilePostDetailDto?> GetProfilePostAsync(Guid id, Teammy.Application.Posts.Dtos.ExpandOptions expand, CancellationToken ct)
        => db.recruitment_posts.AsNoTracking()
            .Where(p => p.post_id == id && p.post_type == "individual")
            .Join(db.semesters.AsNoTracking(), p => p.semester_id, s => s.semester_id, (p, s) => new { p, s })
            .GroupJoin(db.majors.AsNoTracking(), ps => ps.p.major_id, m => m.major_id, (ps, ms) => new { ps, ms })
            .SelectMany(x => x.ms.DefaultIfEmpty(), (x, m) => new { x.ps.p, x.ps.s, m })
            .GroupJoin(db.users.AsNoTracking(), t => t.p.user_id, u => u.user_id, (t, us) => new { t.p, t.s, t.m, us })
            .SelectMany(x => x.us.DefaultIfEmpty(), (x, u) => new { x.p, x.s, x.m, u })
            .Select(x => new ProfilePostDetailDto(
                x.p.post_id,
                x.p.semester_id,
                string.Concat(x.s.season ?? "", " ", (x.s.year.HasValue ? x.s.year.Value.ToString() : "")),
                (expand & Teammy.Application.Posts.Dtos.ExpandOptions.Semester) != 0
                    ? new PostSemesterDto(x.s.semester_id, x.s.season, x.s.year, x.s.start_date, x.s.end_date, x.s.is_active)
                    : null,
                x.p.title,
                x.p.status,
                x.p.post_type,
                x.p.user_id,
                x.u != null ? x.u.display_name : null,
                (expand & Teammy.Application.Posts.Dtos.ExpandOptions.User) != 0 && x.u != null
                    ? new PostUserDto(x.u.user_id, x.u.email, x.u.display_name, x.u.avatar_url, x.u.email_verified)
                    : null,
                x.p.major_id,
                x.m != null ? x.m.major_name : null,
                (expand & Teammy.Application.Posts.Dtos.ExpandOptions.Major) != 0 && x.m != null
                    ? new PostMajorDto(x.m.major_id, x.m.major_name)
                    : null,
                x.p.description,
                x.p.created_at,
                x.p.position_needed // Skills (for individual posts)
            ))
            .FirstOrDefaultAsync(ct);
}
