using Microsoft.EntityFrameworkCore;
using Teammy.Application.Common.Interfaces;
using Teammy.Infrastructure.Persistence;
using Teammy.Infrastructure.Persistence.Models;

namespace Teammy.Infrastructure.Persistence.Repositories;

public sealed class RecruitmentPostRepository(AppDbContext db) : IRecruitmentPostRepository
{
    public async Task<Guid> CreateRecruitmentPostAsync(Guid semesterId, string postType, Guid? groupId, Guid? userId, Guid? majorId, string title, string? description, string? skills, DateTime? applicationDeadline, CancellationToken ct)
    {
        var post = new recruitment_post
        {
            post_id = Guid.NewGuid(),
            semester_id = semesterId,
            post_type = postType,
            group_id = groupId,
            user_id = userId,
            major_id = majorId,
            title = title,
            description = description,
            position_needed = skills,
            status = "open",
            application_deadline = applicationDeadline,
            created_at = DateTime.UtcNow,
            updated_at = DateTime.UtcNow
        };
        db.recruitment_posts.Add(post);
        await db.SaveChangesAsync(ct);
        return post.post_id;
    }

    public async Task<Guid> CreateApplicationAsync(Guid postId, Guid? applicantUserId, Guid? applicantGroupId, Guid appliedByUserId, string? message, CancellationToken ct)
    {
        var c = new candidate
        {
            candidate_id = Guid.NewGuid(),
            post_id = postId,
            applicant_user_id = applicantUserId,
            applicant_group_id = applicantGroupId,
            applied_by_user_id = appliedByUserId,
            message = message,
            status = "pending",
            created_at = DateTime.UtcNow,
            updated_at = DateTime.UtcNow
        };
        db.candidates.Add(c);
        await db.SaveChangesAsync(ct);
        return c.candidate_id;
    }

    public async Task UpdatePostAsync(Guid postId, string? title, string? description, string? skills, string? status, CancellationToken ct)
    {
        var post = await db.recruitment_posts.FirstOrDefaultAsync(x => x.post_id == postId, ct)
            ?? throw new KeyNotFoundException("Post not found");
        if (title is not null) post.title = title;
        if (description is not null) post.description = description;
        if (skills is not null) post.position_needed = skills;
        if (!string.IsNullOrWhiteSpace(status)) post.status = status!;
        post.updated_at = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    public async Task DeletePostAsync(Guid postId, CancellationToken ct)
    {
        var post = await db.recruitment_posts.FirstOrDefaultAsync(x => x.post_id == postId, ct)
            ?? throw new KeyNotFoundException("Post not found");
        db.recruitment_posts.Remove(post);
        await db.SaveChangesAsync(ct);
    }

    public async Task ExpireOpenPostsAsync(DateTime utcNow, CancellationToken ct)
    {
        var toExpire = await db.recruitment_posts
            .Where(p => p.status == "open" && p.application_deadline.HasValue && p.application_deadline <= utcNow)
            .ToListAsync(ct);
        if (toExpire.Count == 0) return;

        foreach (var post in toExpire)
        {
            post.status = "expired";
            post.updated_at = utcNow;
        }

        await db.SaveChangesAsync(ct);
    }

    public async Task UpdateApplicationStatusAsync(Guid applicationId, string newStatus, CancellationToken ct)
    {
        var c = await db.candidates.FirstOrDefaultAsync(x => x.candidate_id == applicationId, ct)
            ?? throw new KeyNotFoundException("Application not found");
        c.status = newStatus;
        c.updated_at = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    public async Task ReactivateApplicationAsync(Guid applicationId, string? message, CancellationToken ct)
    {
        var c = await db.candidates.FirstOrDefaultAsync(x => x.candidate_id == applicationId, ct)
            ?? throw new KeyNotFoundException("Application not found");
        c.status = "pending";
        if (message is not null) c.message = message;
        c.updated_at = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    public async Task<int> CloseAllOpenPostsForGroupAsync(Guid groupId, CancellationToken ct)
    {
        var posts = await db.recruitment_posts.Where(p => p.group_id == groupId && p.status == "open").ToListAsync(ct);
        if (posts.Count == 0) return 0;
        foreach (var p in posts) { p.status = "closed"; p.updated_at = DateTime.UtcNow; }
        return await db.SaveChangesAsync(ct);
    }

    public async Task<int> CloseAllOpenPostsExceptAsync(Guid groupId, Guid keepPostId, CancellationToken ct)
    {
        var posts = await db.recruitment_posts.Where(p => p.group_id == groupId && p.status == "open" && p.post_id != keepPostId).ToListAsync(ct);
        if (posts.Count == 0) return 0;
        foreach (var p in posts) { p.status = "closed"; p.updated_at = DateTime.UtcNow; }
        return await db.SaveChangesAsync(ct);
    }

    public async Task<int> SetOpenPostsStatusForGroupAsync(Guid groupId, string newStatus, CancellationToken ct)
    {
        var posts = await db.recruitment_posts.Where(p => p.group_id == groupId && p.status == "open").ToListAsync(ct);
        if (posts.Count == 0) return 0;
        foreach (var p in posts) { p.status = newStatus; p.updated_at = DateTime.UtcNow; }
        return await db.SaveChangesAsync(ct);
    }

    public async Task<int> RejectPendingApplicationsForUserInGroupAsync(Guid groupId, Guid userId, CancellationToken ct)
    {
        var q = from c in db.candidates
                join p in db.recruitment_posts on c.post_id equals p.post_id
                where p.group_id == groupId && c.applicant_user_id == userId && c.status == "pending"
                select c;
        var items = await q.ToListAsync(ct);
        if (items.Count == 0) return 0;
        foreach (var c in items) { c.status = "rejected"; c.updated_at = DateTime.UtcNow; }
        return await db.SaveChangesAsync(ct);
    }

    public async Task<int> RejectPendingProfileInvitationsAsync(Guid ownerUserId, Guid semesterId, Guid keepCandidateId, CancellationToken ct)
    {
        var q = from c in db.candidates
                join p in db.recruitment_posts on c.post_id equals p.post_id
                where p.user_id == ownerUserId
                      && p.post_type == "individual"
                      && p.semester_id == semesterId
                      && c.status == "pending"
                      && c.candidate_id != keepCandidateId
                select c;
        var items = await q.ToListAsync(ct);
        if (items.Count == 0) return 0;
        foreach (var c in items)
        {
            c.status = "rejected";
            c.updated_at = DateTime.UtcNow;
        }

        return await db.SaveChangesAsync(ct);
    }
}
