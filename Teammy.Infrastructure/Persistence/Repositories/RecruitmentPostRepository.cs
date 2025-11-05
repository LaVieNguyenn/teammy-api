using Microsoft.EntityFrameworkCore;
using Teammy.Application.Common.Interfaces;
using Teammy.Infrastructure.Persistence;
using Teammy.Infrastructure.Persistence.Models;

namespace Teammy.Infrastructure.Persistence.Repositories;

public sealed class RecruitmentPostRepository(AppDbContext db) : IRecruitmentPostRepository
{
    public async Task<Guid> CreateRecruitmentPostAsync(Guid semesterId, string postType, Guid? groupId, Guid? userId, Guid? majorId, string title, string? description, string? skills, CancellationToken ct)
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

    public async Task UpdateApplicationStatusAsync(Guid applicationId, string newStatus, CancellationToken ct)
    {
        var c = await db.candidates.FirstOrDefaultAsync(x => x.candidate_id == applicationId, ct)
            ?? throw new KeyNotFoundException("Application not found");
        c.status = newStatus;
        c.updated_at = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
    }
}

