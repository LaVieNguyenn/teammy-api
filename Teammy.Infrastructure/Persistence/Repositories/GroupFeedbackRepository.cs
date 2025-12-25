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

    public async Task UpdateAsync(Guid feedbackId, GroupFeedbackUpdateModel model, CancellationToken ct)
    {
        var entity = await db.group_feedbacks.FirstOrDefaultAsync(f => f.feedback_id == feedbackId, ct)
            ?? throw new KeyNotFoundException("Feedback not found");

        if (model.Category is not null) entity.category = NormalizeOptional(model.Category);
        if (model.Summary is not null) entity.summary = model.Summary;
        if (model.Details is not null) entity.details = NormalizeOptional(model.Details);
        if (model.Rating.HasValue) entity.rating = model.Rating;
        if (model.Blockers is not null) entity.blockers = NormalizeOptional(model.Blockers);
        if (model.NextSteps is not null) entity.next_steps = NormalizeOptional(model.NextSteps);

        entity.updated_at = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
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

    public async Task DeleteAsync(Guid feedbackId, CancellationToken ct)
    {
        var entity = await db.group_feedbacks.FirstOrDefaultAsync(f => f.feedback_id == feedbackId, ct)
            ?? throw new KeyNotFoundException("Feedback not found");
        db.group_feedbacks.Remove(entity);
        await db.SaveChangesAsync(ct);
    }

    private static string? NormalizeOptional(string value)
    {
        var trimmed = value.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }
}
