using Microsoft.EntityFrameworkCore;
using Teammy.Application.Common.Interfaces;
using Teammy.Application.Feedback.Dtos;
using Teammy.Infrastructure.Persistence;
using Teammy.Infrastructure.Persistence.Models;

namespace Teammy.Infrastructure.Persistence.Repositories;

public sealed class GroupFeedbackReadOnlyQueries(AppDbContext db) : IGroupFeedbackReadOnlyQueries
{
    public async Task<IReadOnlyList<GroupFeedbackDto>> ListForGroupAsync(Guid groupId, string? status, int skip, int take, CancellationToken ct)
    {
        var query = from f in db.group_feedbacks.AsNoTracking()
                    join mentor in db.users.AsNoTracking() on f.mentor_id equals mentor.user_id
                    where f.group_id == groupId
                    orderby f.created_at descending
                    select new { f, mentor };

        if (!string.IsNullOrWhiteSpace(status))
            query = query.Where(x => x.f.status == status);

        var items = await query
            .Select(x => BuildDto(x.f, x.mentor))
            .Skip(skip)
            .Take(take)
            .ToListAsync(ct);
        return items;
    }

    public Task<int> CountForGroupAsync(Guid groupId, string? status, CancellationToken ct)
    {
        var query = db.group_feedbacks.AsNoTracking().Where(f => f.group_id == groupId);
        if (!string.IsNullOrWhiteSpace(status))
            query = query.Where(f => f.status == status);
        return query.CountAsync(ct);
    }

    public Task<GroupFeedbackDto?> GetAsync(Guid feedbackId, CancellationToken ct)
    {
        var query = from f in db.group_feedbacks.AsNoTracking()
                    join mentor in db.users.AsNoTracking() on f.mentor_id equals mentor.user_id
                    where f.feedback_id == feedbackId
                    select BuildDto(f, mentor);

        return query.FirstOrDefaultAsync(ct);
    }

    private static GroupFeedbackDto BuildDto(group_feedback f, user mentor)
        => new(
            f.feedback_id,
            f.group_id,
            f.mentor_id,
            mentor.display_name,
            mentor.email,
            mentor.avatar_url,
            f.category,
            f.summary,
            f.details,
            f.rating,
            f.blockers,
            f.next_steps,
            f.status,
            f.acknowledged_note,
            f.created_at,
            f.updated_at,
            f.acknowledged_at);
}
