using Microsoft.EntityFrameworkCore;
using Teammy.Application.Common.Interfaces;
using Teammy.Application.Feedback.Dtos;
using Teammy.Infrastructure.Persistence;
using Teammy.Infrastructure.Persistence.Models;

namespace Teammy.Infrastructure.Persistence.Repositories;

public sealed class GroupFeedbackRepository(AppDbContext db) : IGroupFeedbackRepository
{
    public async Task<Guid> CreateAsync(GroupFeedbackCreateModel model, CancellationToken ct)
    {
        var entity = new group_feedback
        {
            group_id = model.GroupId,
            semester_id = model.SemesterId,
            mentor_id = model.MentorId,
            category = model.Category,
            summary = model.Summary,
            details = model.Details,
            rating = model.Rating,
            blockers = model.Blockers,
            next_steps = model.NextSteps,
            status = "submitted",
            created_at = DateTime.UtcNow,
            updated_at = DateTime.UtcNow
        };

        db.group_feedbacks.Add(entity);
        await db.SaveChangesAsync(ct);
        return entity.feedback_id;
    }

    public async Task UpdateStatusAsync(Guid feedbackId, string status, Guid? acknowledgedByUserId, string? note, CancellationToken ct)
    {
        var entity = await db.group_feedbacks.FirstOrDefaultAsync(f => f.feedback_id == feedbackId, ct)
            ?? throw new KeyNotFoundException("Feedback not found");

        entity.status = status;
        entity.acknowledged_by = acknowledgedByUserId;
        entity.acknowledged_note = note;
        entity.acknowledged_at = acknowledgedByUserId.HasValue ? DateTime.UtcNow : null;
        entity.updated_at = DateTime.UtcNow;

        await db.SaveChangesAsync(ct);
    }
}
