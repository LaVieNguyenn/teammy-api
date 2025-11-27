using Microsoft.EntityFrameworkCore;
using Teammy.Application.Common.Interfaces;
using Teammy.Application.Invitations.Dtos;
using Teammy.Infrastructure.Persistence;

namespace Teammy.Infrastructure.Persistence.Repositories;

public sealed class InvitationReadOnlyQueries(AppDbContext db) : IInvitationReadOnlyQueries
{
    public Task<InvitationDetailDto?> GetAsync(Guid invitationId, CancellationToken ct)
        => (from i in db.invitations.AsNoTracking()
            join g in db.groups.AsNoTracking() on i.group_id equals g.group_id
            join u in db.users.AsNoTracking() on i.invitee_user_id equals u.user_id
            join t in db.topics.AsNoTracking() on i.topic_id equals t.topic_id into topicJoin
            from t in topicJoin.DefaultIfEmpty()
            where i.invitation_id == invitationId
            select new InvitationDetailDto(
                i.invitation_id,
                i.invitee_user_id,
                i.invited_by,
                i.topic_id != null ? "mentor" : "member",
                i.status,
                i.created_at,
                i.responded_at,
                i.expires_at,
                g.group_id,
                g.semester_id,
                g.name,
                u.email,
                i.topic_id,
                t != null ? t.title : null
            )).FirstOrDefaultAsync(ct);

    public Task<IReadOnlyList<InvitationListItemDto>> ListForUserAsync(Guid userId, string? status, CancellationToken ct)
    {
        var q = from i in db.invitations.AsNoTracking()
                join g in db.groups.AsNoTracking() on i.group_id equals g.group_id
                join invBy in db.users.AsNoTracking() on i.invited_by equals invBy.user_id
                join t in db.topics.AsNoTracking() on i.topic_id equals t.topic_id into topicJoin
                from t in topicJoin.DefaultIfEmpty()
                where i.invitee_user_id == userId
                  && ((status == null || status == "") || i.status == status)
                orderby i.created_at descending
                select new InvitationListItemDto(
                    i.invitation_id,
                    i.topic_id != null ? "mentor" : "member",
                    i.status,
                    i.created_at,
                    i.expires_at,
                    i.invited_by,
                    invBy.display_name,
                    g.group_id,
                    g.name,
                    i.topic_id,
                    t != null ? t.title : null
                );
        return q.ToListAsync(ct).ContinueWith(t => (IReadOnlyList<InvitationListItemDto>)t.Result, ct);
    }

    public Task<Guid?> FindPendingIdAsync(Guid groupId, Guid inviteeUserId, CancellationToken ct)
        => db.invitations.AsNoTracking()
            .Where(i => i.group_id == groupId && i.invitee_user_id == inviteeUserId && i.status == "pending")
            .Select(i => (Guid?)i.invitation_id)
            .FirstOrDefaultAsync(ct);

    public Task<(Guid InvitationId, string Status, Guid? TopicId)?> FindAnyAsync(Guid groupId, Guid inviteeUserId, CancellationToken ct)
        => db.invitations.AsNoTracking()
            .Where(i => i.group_id == groupId && i.invitee_user_id == inviteeUserId)
            .Select(i => new ValueTuple<Guid, string, Guid?>(i.invitation_id, i.status, i.topic_id))
            .FirstOrDefaultAsync(ct)
            .ContinueWith(t => t.Result == default ? (ValueTuple<Guid,string, Guid?>?)null : t.Result, ct);
}
