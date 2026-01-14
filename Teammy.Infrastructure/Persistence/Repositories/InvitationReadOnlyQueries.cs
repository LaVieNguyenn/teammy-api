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
                i.topic_id != null
                    ? (i.invited_by == i.invitee_user_id ? "mentor_request" : "mentor")
                    : "member",
                i.status,
                i.created_at,
                i.responded_at,
                i.expires_at,
                g.group_id,
                g.semester_id,
                g.name,
                u.email,
                i.topic_id,
                t != null ? t.title : null,
                i.message
            )).FirstOrDefaultAsync(ct);

    public Task<IReadOnlyList<InvitationListItemDto>> ListForUserAsync(Guid userId, string? status, Guid? semesterId, Guid? majorId, CancellationToken ct)
    {
        var q = from i in db.invitations.AsNoTracking()
                join g in db.groups.AsNoTracking() on i.group_id equals g.group_id
                join invBy in db.users.AsNoTracking() on i.invited_by equals invBy.user_id
                join s in db.semesters.AsNoTracking() on g.semester_id equals s.semester_id
                join m in db.majors.AsNoTracking() on g.major_id equals m.major_id into majorJoin
                from m in majorJoin.DefaultIfEmpty()
                join t in db.topics.AsNoTracking() on i.topic_id equals t.topic_id into topicJoin
                from t in topicJoin.DefaultIfEmpty()
                where i.invitee_user_id == userId
                  && ((status == null || status == "") || i.status == status)
                  && (!semesterId.HasValue || g.semester_id == semesterId.Value)
                  && (!majorId.HasValue || g.major_id == majorId.Value)
                orderby i.created_at descending
                select new InvitationListItemDto(
                    i.invitation_id,
                    i.topic_id != null
                        ? (i.invited_by == i.invitee_user_id ? "mentor_request" : "mentor")
                        : "member",
                    i.status,
                    i.created_at,
                i.expires_at,
                i.invited_by,
                invBy.display_name,
                g.group_id,
                g.name,
                s.semester_id,
                string.Concat(s.season ?? "", " ", (s.year.HasValue ? s.year.Value.ToString() : "")),
                g.major_id,
                m != null ? m.major_name : null,
                i.topic_id,
                t != null ? t.title : null,
                i.message
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
            .OrderByDescending(i => i.created_at)
            .Select(i => new ValueTuple<Guid, string, Guid?>(i.invitation_id, i.status, i.topic_id))
            .FirstOrDefaultAsync(ct)
            .ContinueWith(t => t.Result == default ? (ValueTuple<Guid,string, Guid?>?)null : t.Result, ct);

    public Task<Guid?> GetPendingMentorTopicAsync(Guid groupId, CancellationToken ct)
        => db.invitations.AsNoTracking()
            .Where(i => i.group_id == groupId && i.status == "pending" && i.topic_id != null)
            .OrderByDescending(i => i.created_at)
            .Select(i => (Guid?)i.topic_id)
            .FirstOrDefaultAsync(ct);

    public async Task<IReadOnlyList<(Guid InvitationId, Guid InviteeUserId, Guid InvitedBy)>> ListPendingMentorInvitesForGroupTopicAsync(
        Guid groupId,
        Guid topicId,
        Guid exceptInvitationId,
        CancellationToken ct)
    {
        var items = await db.invitations.AsNoTracking()
            .Where(i => i.group_id == groupId
                        && i.topic_id == topicId
                        && i.invitation_id != exceptInvitationId
                        && i.status == "pending")
            .Select(i => new ValueTuple<Guid, Guid, Guid>(i.invitation_id, i.invitee_user_id, i.invited_by))
            .ToListAsync(ct);
        return items;
    }
}
