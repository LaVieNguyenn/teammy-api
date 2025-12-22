using System.Text.Json;
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
            .GroupJoin(db.majors.AsNoTracking(), t => t.g != null ? (Guid?)t.g.major_id : null, gm => (Guid?)gm.major_id, (t, gms) => new { t.p, t.s, t.g, t.m, gms })
            .SelectMany(x => x.gms.DefaultIfEmpty(), (x, gm) => new { x.p, x.s, x.g, x.m, gm })
            .GroupJoin(db.topics.AsNoTracking(), t => t.g != null ? (Guid?)t.g.topic_id : null, tp => (Guid?)tp.topic_id, (t, tps) => new { t.p, t.s, t.g, t.m, t.gm, tps })
            .SelectMany(x => x.tps.DefaultIfEmpty(), (x, topic) => new { x.p, x.s, x.g, x.m, x.gm, topic })
            .GroupJoin(db.users.AsNoTracking(), t => t.g != null ? (Guid?)t.g.mentor_id : null, u => (Guid?)u.user_id, (t, mentors) => new { t.p, t.s, t.g, t.m, t.gm, t.topic, mentors })
            .SelectMany(x => x.mentors.DefaultIfEmpty(), (x, mentor) => new { x.p, x.s, x.g, x.m, x.gm, x.topic, mentor })
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
                    ? new PostGroupDto(
                        x.g.group_id,
                        x.g.semester_id,
                        x.g.mentor_id,
                        x.g.name,
                        x.g.description,
                        x.g.status,
                        x.g.max_members,
                        x.g.major_id,
                        x.g.topic_id,
                        x.g.created_at,
                        x.g.updated_at,
                        x.gm != null ? new PostMajorDto(x.gm.major_id, x.gm.major_name) : null,
                        x.topic != null ? new PostTopicDto(
                            x.topic.topic_id,
                            x.topic.semester_id,
                            x.topic.major_id,
                            x.topic.title,
                            x.topic.description,
                            x.topic.status,
                            x.topic.created_by,
                            x.topic.created_at) : null,
                        x.mentor != null ? new PostUserDto(
                            x.mentor.user_id,
                            x.mentor.email!,
                            x.mentor.display_name!,
                            x.mentor.avatar_url,
                            x.mentor.email_verified) : null)
                    : null,
                x.p.major_id,
                x.m != null ? x.m.major_name : null,
                (expand & Teammy.Application.Posts.Dtos.ExpandOptions.Major) != 0 && x.m != null
                    ? new PostMajorDto(x.m.major_id, x.m.major_name)
                    : null,
                x.p.description,
                x.p.position_needed,
                ParseSkills(x.p.required_skills),
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
        // skill filtering moved to service layer to avoid case-sensitivity issues in JSON storage

        return await q
            .OrderByDescending(p => p.created_at)
            .Join(db.semesters.AsNoTracking(), p => p.semester_id, s => s.semester_id, (p, s) => new { p, s })
            .GroupJoin(db.groups.AsNoTracking(), ps => ps.p.group_id, g => g.group_id, (ps, grps) => new { ps, grps })
            .SelectMany(x => x.grps.DefaultIfEmpty(), (x, g) => new { x.ps.p, x.ps.s, g })
            .GroupJoin(db.majors.AsNoTracking(), t => t.p.major_id, m => m.major_id, (t, ms) => new { t.p, t.s, t.g, ms })
            .SelectMany(x => x.ms.DefaultIfEmpty(), (x, m) => new { x.p, x.s, x.g, m })
            .GroupJoin(db.majors.AsNoTracking(), t => t.g != null ? (Guid?)t.g.major_id : null, gm => (Guid?)gm.major_id, (t, gms) => new { t.p, t.s, t.g, t.m, gms })
            .SelectMany(x => x.gms.DefaultIfEmpty(), (x, gm) => new { x.p, x.s, x.g, x.m, gm })
            .GroupJoin(db.topics.AsNoTracking(), t => t.g != null ? (Guid?)t.g.topic_id : null, tp => (Guid?)tp.topic_id, (t, tps) => new { t.p, t.s, t.g, t.m, t.gm, tps })
            .SelectMany(x => x.tps.DefaultIfEmpty(), (x, topic) => new { x.p, x.s, x.g, x.m, x.gm, topic })
            .GroupJoin(db.users.AsNoTracking(), t => t.g != null ? (Guid?)t.g.mentor_id : null, u => (Guid?)u.user_id, (t, mentors) => new { t.p, t.s, t.g, t.m, t.gm, t.topic, mentors })
            .SelectMany(x => x.mentors.DefaultIfEmpty(), (x, mentor) => new { x.p, x.s, x.g, x.m, x.gm, x.topic, mentor })
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
                    ? new PostGroupDto(
                        x.g.group_id,
                        x.g.semester_id,
                        x.g.mentor_id,
                        x.g.name,
                        x.g.description,
                        x.g.status,
                        x.g.max_members,
                        x.g.major_id,
                        x.g.topic_id,
                        x.g.created_at,
                        x.g.updated_at,
                        x.gm != null ? new PostMajorDto(x.gm.major_id, x.gm.major_name) : null,
                        x.topic != null ? new PostTopicDto(
                            x.topic.topic_id,
                            x.topic.semester_id,
                            x.topic.major_id,
                            x.topic.title,
                            x.topic.description,
                            x.topic.status,
                            x.topic.created_by,
                            x.topic.created_at) : null,
                        x.mentor != null ? new PostUserDto(
                            x.mentor.user_id,
                            x.mentor.email!,
                            x.mentor.display_name!,
                            x.mentor.avatar_url,
                            x.mentor.email_verified) : null)
                    : null,
                x.p.major_id,
                x.m != null ? x.m.major_name : null,
                (expand & Teammy.Application.Posts.Dtos.ExpandOptions.Major) != 0 && x.m != null
                    ? new PostMajorDto(x.m.major_id, x.m.major_name)
                    : null,
                x.p.position_needed,
                ParseSkills(x.p.required_skills),
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

    public async Task<IReadOnlyList<RecruitmentPostSummaryDto>> ListByGroupAsync(Guid groupId, Teammy.Application.Posts.Dtos.ExpandOptions expand, Guid? currentUserId, CancellationToken ct)
    {
        var q = db.recruitment_posts.AsNoTracking()
            .Where(p => p.group_id == groupId);

        return await q
            .OrderByDescending(p => p.created_at)
            .Join(db.semesters.AsNoTracking(), p => p.semester_id, s => s.semester_id, (p, s) => new { p, s })
            .GroupJoin(db.groups.AsNoTracking(), ps => ps.p.group_id, g => g.group_id, (ps, grps) => new { ps, grps })
            .SelectMany(x => x.grps.DefaultIfEmpty(), (x, g) => new { x.ps.p, x.ps.s, g })
            .GroupJoin(db.majors.AsNoTracking(), t => t.p.major_id, m => m.major_id, (t, ms) => new { t.p, t.s, t.g, ms })
            .SelectMany(x => x.ms.DefaultIfEmpty(), (x, m) => new { x.p, x.s, x.g, m })
            .GroupJoin(db.majors.AsNoTracking(), t => t.g != null ? (Guid?)t.g.major_id : null, gm => (Guid?)gm.major_id, (t, gms) => new { t.p, t.s, t.g, t.m, gms })
            .SelectMany(x => x.gms.DefaultIfEmpty(), (x, gm) => new { x.p, x.s, x.g, x.m, gm })
            .GroupJoin(db.topics.AsNoTracking(), t => t.g != null ? (Guid?)t.g.topic_id : null, tp => (Guid?)tp.topic_id, (t, tps) => new { t.p, t.s, t.g, t.m, t.gm, tps })
            .SelectMany(x => x.tps.DefaultIfEmpty(), (x, topic) => new { x.p, x.s, x.g, x.m, x.gm, topic })
            .GroupJoin(db.users.AsNoTracking(), t => t.g != null ? (Guid?)t.g.mentor_id : null, u => (Guid?)u.user_id, (t, mentors) => new { t.p, t.s, t.g, t.m, t.gm, t.topic, mentors })
            .SelectMany(x => x.mentors.DefaultIfEmpty(), (x, mentor) => new { x.p, x.s, x.g, x.m, x.gm, x.topic, mentor })
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
                    ? new PostGroupDto(
                        x.g.group_id,
                        x.g.semester_id,
                        x.g.mentor_id,
                        x.g.name,
                        x.g.description,
                        x.g.status,
                        x.g.max_members,
                        x.g.major_id,
                        x.g.topic_id,
                        x.g.created_at,
                        x.g.updated_at,
                        x.gm != null ? new PostMajorDto(x.gm.major_id, x.gm.major_name) : null,
                        x.topic != null ? new PostTopicDto(
                            x.topic.topic_id,
                            x.topic.semester_id,
                            x.topic.major_id,
                            x.topic.title,
                            x.topic.description,
                            x.topic.status,
                            x.topic.created_by,
                            x.topic.created_at) : null,
                        x.mentor != null ? new PostUserDto(
                            x.mentor.user_id,
                            x.mentor.email!,
                            x.mentor.display_name!,
                            x.mentor.avatar_url,
                            x.mentor.email_verified) : null)
                    : null,
                x.p.major_id,
                x.m != null ? x.m.major_name : null,
                (expand & Teammy.Application.Posts.Dtos.ExpandOptions.Major) != 0 && x.m != null
                    ? new PostMajorDto(x.m.major_id, x.m.major_name)
                    : null,
                x.p.position_needed,
                ParseSkills(x.p.required_skills),
                x.g != null
                    ? db.group_members.Count(mb => mb.group_id == x.g.group_id && (mb.status == "member" || mb.status == "leader"))
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

    public Task<(Guid? GroupId, Guid SemesterId, Guid? OwnerUserId, DateTime? ApplicationDeadline, string Status)> GetPostOwnerAsync(Guid postId, CancellationToken ct)
        => db.recruitment_posts.AsNoTracking()
            .Where(p => p.post_id == postId)
            .Select(p => new ValueTuple<Guid?, Guid, Guid?, DateTime?, string>(p.group_id, p.semester_id, p.user_id, p.application_deadline, p.status))
            .FirstOrDefaultAsync(ct);

    public Task<(Guid ApplicationId, Guid PostId)?> FindPendingApplicationInGroupAsync(Guid groupId, Guid userId, CancellationToken ct)
        => (from c in db.candidates.AsNoTracking()
            join p in db.recruitment_posts.AsNoTracking() on c.post_id equals p.post_id
            where p.group_id == groupId && c.applicant_user_id == userId && c.status == "pending"
            orderby c.created_at descending
            select new ValueTuple<Guid, Guid>(c.candidate_id, c.post_id))
            .FirstOrDefaultAsync(ct)
            .ContinueWith(t => t.Result == default ? (ValueTuple<Guid, Guid>?)null : t.Result, ct);

    public Task<(Guid ApplicationId, string Status)?> FindApplicationByPostAndUserAsync(Guid postId, Guid userId, CancellationToken ct)
        => db.candidates.AsNoTracking()
            .Where(c => c.post_id == postId && c.applicant_user_id == userId)
            .OrderByDescending(c => c.created_at)
            .Select(c => new ValueTuple<Guid, string>(c.candidate_id, c.status))
            .FirstOrDefaultAsync(ct)
            .ContinueWith(t => t.Result == default ? (ValueTuple<Guid, string>?)null : t.Result, ct);
    public Task<(Guid ApplicationId, string Status)?> FindApplicationByPostAndGroupAsync(Guid postId, Guid groupId, CancellationToken ct)
        => db.candidates.AsNoTracking()
            .Where(c => c.post_id == postId && c.applicant_group_id == groupId)
            .OrderByDescending(c => c.created_at)
            .Select(c => new ValueTuple<Guid, string>(c.candidate_id, c.status))
            .FirstOrDefaultAsync(ct)
            .ContinueWith(t => t.Result == default ? (ValueTuple<Guid, string>?)null : t.Result, ct);

    public async Task<IReadOnlyList<ProfilePostInvitationDto>> ListProfileInvitationsAsync(Guid ownerUserId, string? status, CancellationToken ct)
    {
        var q =
            from c in db.candidates.AsNoTracking()
            join p in db.recruitment_posts.AsNoTracking() on c.post_id equals p.post_id
            join g in db.groups.AsNoTracking() on c.applicant_group_id equals g.group_id
            where p.user_id == ownerUserId && p.post_type == "individual" && c.applicant_group_id != null
            select new { c, p, g };

        if (!string.IsNullOrWhiteSpace(status))
        {
            q = q.Where(x => x.c.status == status);
        }

        var query =
            from item in q
            join m in db.majors.AsNoTracking() on item.g.major_id equals m.major_id into mj
            from m in mj.DefaultIfEmpty()
            join gm in db.group_members.AsNoTracking().Where(mm => mm.status == "leader") on item.g.group_id equals gm.group_id into gmj
            from gm in gmj.DefaultIfEmpty()
            join leader in db.users.AsNoTracking() on gm.user_id equals leader.user_id into lj
            from leader in lj.DefaultIfEmpty()
            orderby item.c.created_at descending
            select new ProfilePostInvitationDto(
                item.c.candidate_id,
                item.p.post_id,
                item.g.group_id,
                item.g.name,
                item.c.status,
                item.c.created_at,
                item.p.semester_id,
                item.g.major_id,
                m != null ? m.major_name : null,
                leader != null ? (Guid?)leader.user_id : null,
                leader != null ? leader.display_name : null,
                leader != null ? leader.email : null
            );

    return await query.ToListAsync(ct);
    }

    public Task<ProfilePostInvitationDetail?> GetProfileInvitationAsync(Guid candidateId, Guid ownerUserId, CancellationToken ct)
        => (from c in db.candidates.AsNoTracking()
            join p in db.recruitment_posts.AsNoTracking() on c.post_id equals p.post_id
            join g in db.groups.AsNoTracking() on c.applicant_group_id equals g.group_id
            where c.candidate_id == candidateId
                  && p.post_type == "individual"
                  && p.user_id == ownerUserId
                  && c.applicant_group_id != null
            select new ProfilePostInvitationDetail(
                c.candidate_id,
                p.post_id,
                g.group_id,
                p.semester_id,
                g.major_id,
                c.status))
            .FirstOrDefaultAsync(ct);

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
            .GroupJoin(db.majors.AsNoTracking(), t => t.g != null ? (Guid?)t.g.major_id : null, gm => (Guid?)gm.major_id, (t, gms) => new { t.p, t.s, t.g, t.m, gms })
            .SelectMany(x => x.gms.DefaultIfEmpty(), (x, gm) => new { x.p, x.s, x.g, x.m, gm })
            .GroupJoin(db.topics.AsNoTracking(), t => t.g != null ? (Guid?)t.g.topic_id : null, tp => (Guid?)tp.topic_id, (t, tps) => new { t.p, t.s, t.g, t.m, t.gm, tps })
            .SelectMany(x => x.tps.DefaultIfEmpty(), (x, topic) => new { x.p, x.s, x.g, x.m, x.gm, topic })
            .GroupJoin(db.users.AsNoTracking(), t => t.g != null ? (Guid?)t.g.mentor_id : null, u => (Guid?)u.user_id, (t, mentors) => new { t.p, t.s, t.g, t.m, t.gm, t.topic, mentors })
            .SelectMany(x => x.mentors.DefaultIfEmpty(), (x, mentor) => new { x.p, x.s, x.g, x.m, x.gm, x.topic, mentor })
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
                    ? new PostGroupDto(
                        x.g.group_id,
                        x.g.semester_id,
                        x.g.mentor_id,
                        x.g.name,
                        x.g.description,
                        x.g.status,
                        x.g.max_members,
                        x.g.major_id,
                        x.g.topic_id,
                        x.g.created_at,
                        x.g.updated_at,
                        x.gm != null ? new PostMajorDto(x.gm.major_id, x.gm.major_name) : null,
                        x.topic != null ? new PostTopicDto(
                            x.topic.topic_id,
                            x.topic.semester_id,
                            x.topic.major_id,
                            x.topic.title,
                            x.topic.description,
                            x.topic.status,
                            x.topic.created_by,
                            x.topic.created_at) : null,
                        x.mentor != null ? new PostUserDto(
                            x.mentor.user_id,
                            x.mentor.email!,
                            x.mentor.display_name!,
                            x.mentor.avatar_url,
                            x.mentor.email_verified) : null)
                    : null,
                x.p.major_id,
                x.m != null ? x.m.major_name : null,
                (expand & Teammy.Application.Posts.Dtos.ExpandOptions.Major) != 0 && x.m != null
                    ? new PostMajorDto(x.m.major_id, x.m.major_name)
                    : null,
                x.p.position_needed,
                ParseSkills(x.p.required_skills),
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

    public async Task<IReadOnlyList<ProfilePostSummaryDto>> ListProfilePostsAsync(string? skills, Guid? majorId, string? status, Teammy.Application.Posts.Dtos.ExpandOptions expand, Guid? currentUserId, CancellationToken ct)
    {
        var q = db.recruitment_posts.AsNoTracking()
            .Where(p => p.post_type == "individual"
                        && !db.group_members.Any(m =>
                            m.user_id == p.user_id
                            && m.semester_id == p.semester_id
                            && (m.status == "member" || m.status == "leader")));
        if (!string.IsNullOrWhiteSpace(status)) q = q.Where(p => p.status == status);
        if (majorId.HasValue) q = q.Where(p => p.major_id == majorId);
        // skill filtering handled in service layer

        return await q
            .OrderByDescending(p => p.created_at)
            .Join(db.semesters.AsNoTracking(), p => p.semester_id, s => s.semester_id, (p, s) => new { p, s })
            .GroupJoin(db.majors.AsNoTracking(), ps => ps.p.major_id, m => m.major_id, (ps, ms) => new { ps, ms })
            .SelectMany(x => x.ms.DefaultIfEmpty(), (x, m) => new { x.ps.p, x.ps.s, m })
            .GroupJoin(db.users.AsNoTracking(), t => t.p.user_id, u => u.user_id, (t, us) => new { t.p, t.s, t.m, us })
            .SelectMany(x => x.us.DefaultIfEmpty(), (x, u) => new { x.p, x.s, x.m, u })
            .GroupJoin(db.majors.AsNoTracking(), t => t.u != null ? (Guid?)t.u.major_id : null, um => (Guid?)um.major_id, (t, ums) => new { t.p, t.s, t.m, t.u, ums })
            .SelectMany(x => x.ums.DefaultIfEmpty(), (x, um) => new { x.p, x.s, x.m, x.u, um })
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
                    ? new ProfilePostUserDto(
                        x.u.user_id,
                        x.u.email,
                        x.u.display_name,
                        x.u.avatar_url,
                        x.u.email_verified,
                        x.u.phone,
                        x.u.student_code,
                        x.u.gender,
                        x.u.major_id,
                        x.um != null ? x.um.major_name : null,
                        x.u.skills,
                        x.u.skills_completed,
                        x.u.is_active,
                        x.u.created_at,
                        x.u.updated_at)
                    : null,
                x.p.major_id,
                x.m != null ? x.m.major_name : null,
                (expand & Teammy.Application.Posts.Dtos.ExpandOptions.Major) != 0 && x.m != null
                    ? new PostMajorDto(x.m.major_id, x.m.major_name)
                    : null,
                x.p.description,
                x.p.position_needed,
                x.p.created_at,
                currentUserId.HasValue &&
                    db.candidates.Any(c =>
                        c.post_id == x.p.post_id &&
                        (c.applied_by_user_id == currentUserId.Value ||
                         (x.p.user_id == currentUserId.Value && c.applicant_group_id != null))),
                currentUserId.HasValue
                    ? db.candidates
                        .Where(c =>
                            c.post_id == x.p.post_id &&
                            (c.applied_by_user_id == currentUserId.Value ||
                             (x.p.user_id == currentUserId.Value && c.applicant_group_id != null)))
                        .OrderByDescending(c => c.created_at)
                        .Select(c => (Guid?)c.candidate_id)
                        .FirstOrDefault()
                    : null,
                currentUserId.HasValue
                    ? db.candidates
                        .Where(c =>
                            c.post_id == x.p.post_id &&
                            (c.applied_by_user_id == currentUserId.Value ||
                             (x.p.user_id == currentUserId.Value && c.applicant_group_id != null)))
                        .OrderByDescending(c => c.created_at)
                        .Select(c => c.status)
                        .FirstOrDefault()
                    : null
            ))
            .ToListAsync(ct);
    }

    public Task<ProfilePostDetailDto?> GetProfilePostAsync(Guid id, Teammy.Application.Posts.Dtos.ExpandOptions expand, Guid? currentUserId, CancellationToken ct)
        => db.recruitment_posts.AsNoTracking()
            .Where(p => p.post_id == id
                        && p.post_type == "individual"
                        && !db.group_members.Any(m =>
                            m.user_id == p.user_id
                            && m.semester_id == p.semester_id
                            && (m.status == "member" || m.status == "leader")))
            .Join(db.semesters.AsNoTracking(), p => p.semester_id, s => s.semester_id, (p, s) => new { p, s })
            .GroupJoin(db.majors.AsNoTracking(), ps => ps.p.major_id, m => m.major_id, (ps, ms) => new { ps, ms })
            .SelectMany(x => x.ms.DefaultIfEmpty(), (x, m) => new { x.ps.p, x.ps.s, m })
            .GroupJoin(db.users.AsNoTracking(), t => t.p.user_id, u => u.user_id, (t, us) => new { t.p, t.s, t.m, us })
            .SelectMany(x => x.us.DefaultIfEmpty(), (x, u) => new { x.p, x.s, x.m, u })
            .GroupJoin(db.position_lists.AsNoTracking(),
                t => t.u != null ? t.u.desired_position_id : null,
                pos => (Guid?)pos.position_id,
                (t, poss) => new { t.p, t.s, t.m, t.u, poss })
            .SelectMany(x => x.poss.DefaultIfEmpty(), (x, pos) => new { x.p, x.s, x.m, x.u, pos })
            .GroupJoin(db.mv_students_pools.AsNoTracking(),
                t => new { user_id = t.u != null ? (Guid?)t.u.user_id : null, semester_id = (Guid?)t.p.semester_id },
                sp => new { user_id = sp.user_id, semester_id = sp.semester_id },
                (t, sps) => new { t.p, t.s, t.m, t.u, t.pos, sps })
            .SelectMany(x => x.sps.DefaultIfEmpty(), (x, sp) => new { x.p, x.s, x.m, x.u, x.pos, SpDesiredPositionName = sp.desired_position_name })
            .GroupJoin(db.majors.AsNoTracking(), t => t.u != null ? (Guid?)t.u.major_id : null, um => (Guid?)um.major_id, (t, ums) => new { t.p, t.s, t.m, t.u, t.pos, t.SpDesiredPositionName, ums })
            .SelectMany(x => x.ums.DefaultIfEmpty(), (x, um) => new { x.p, x.s, x.m, x.u, x.pos, x.SpDesiredPositionName, um })
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
                    ? new ProfilePostUserDto(
                        x.u.user_id,
                        x.u.email,
                        x.u.display_name,
                        x.u.avatar_url,
                        x.u.email_verified,
                        x.u.phone,
                        x.u.student_code,
                        x.u.gender,
                        x.u.major_id,
                        x.um != null ? x.um.major_name : null,
                        x.u.skills,
                        x.u.skills_completed,
                        x.u.is_active,
                        x.u.created_at,
                        x.u.updated_at)
                    : null,
                x.p.major_id,
                x.m != null ? x.m.major_name : null,
                (expand & Teammy.Application.Posts.Dtos.ExpandOptions.Major) != 0 && x.m != null
                    ? new PostMajorDto(x.m.major_id, x.m.major_name)
                    : null,
                x.p.description,
                x.p.created_at,
                x.p.position_needed,
                currentUserId.HasValue &&
                    db.candidates.Any(c =>
                        c.post_id == x.p.post_id &&
                        (c.applied_by_user_id == currentUserId.Value ||
                         (x.p.user_id == currentUserId.Value && c.applicant_group_id != null))),
                currentUserId.HasValue
                    ? db.candidates
                        .Where(c =>
                            c.post_id == x.p.post_id &&
                            (c.applied_by_user_id == currentUserId.Value ||
                             (x.p.user_id == currentUserId.Value && c.applicant_group_id != null)))
                        .OrderByDescending(c => c.created_at)
                        .Select(c => (Guid?)c.candidate_id)
                        .FirstOrDefault()
                    : null,
                currentUserId.HasValue
                    ? db.candidates
                        .Where(c =>
                            c.post_id == x.p.post_id &&
                            (c.applied_by_user_id == currentUserId.Value ||
                             (x.p.user_id == currentUserId.Value && c.applicant_group_id != null)))
                        .OrderByDescending(c => c.created_at)
                        .Select(c => c.status)
                        .FirstOrDefault()
                    : null,
                x.pos != null ? x.pos.position_name : x.SpDesiredPositionName
            ))
            .FirstOrDefaultAsync(ct);
    private static IReadOnlyList<string>? ParseSkills(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            var list = JsonSerializer.Deserialize<List<string>>(json);
            if (list is null || list.Count == 0) return null;
            return list;
        }
        catch
        {
            return null;
        }
    }
}
