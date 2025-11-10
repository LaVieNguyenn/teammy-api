using Microsoft.EntityFrameworkCore;
using Teammy.Application.Common.Interfaces;
using Teammy.Application.Invitations.Dtos;
using Teammy.Infrastructure.Persistence;

namespace Teammy.Infrastructure.Persistence.Repositories;

public sealed class InvitationReadOnlyQueries(AppDbContext db) : IInvitationReadOnlyQueries
{
    public Task<InvitationDetailDto?> GetAsync(Guid invitationId, CancellationToken ct)
        => (from i in db.invitations.AsNoTracking()
            join p in db.recruitment_posts.AsNoTracking() on i.post_id equals p.post_id
            join g in db.groups.AsNoTracking() on p.group_id equals g.group_id into gg
            from g in gg.DefaultIfEmpty()
            join u in db.users.AsNoTracking() on i.invitee_user_id equals u.user_id
            where i.invitation_id == invitationId
            select new InvitationDetailDto(
                i.invitation_id,
                i.post_id,
                i.invitee_user_id,
                i.invited_by,
                i.status,
                i.created_at,
                i.responded_at,
                i.expires_at,
                p.group_id,
                p.semester_id,
                g != null ? g.name : null,
                u.email
            )).FirstOrDefaultAsync(ct);

    public Task<IReadOnlyList<InvitationListItemDto>> ListForUserAsync(Guid userId, string? status, CancellationToken ct)
    {
        var q = from i in db.invitations.AsNoTracking()
                join p in db.recruitment_posts.AsNoTracking() on i.post_id equals p.post_id
                join g in db.groups.AsNoTracking() on p.group_id equals g.group_id into gg
                from g in gg.DefaultIfEmpty()
                join invBy in db.users.AsNoTracking() on i.invited_by equals invBy.user_id
                where i.invitee_user_id == userId
                  && ((status == null || status == "") || i.status == status)
                orderby i.created_at descending
                select new InvitationListItemDto(
                    i.invitation_id,
                    i.post_id,
                    i.status,
                    i.created_at,
                    i.expires_at,
                    i.invited_by,
                    invBy.display_name,
                    p.group_id,
                    g != null ? g.name : null
                );
        return q.ToListAsync(ct).ContinueWith(t => (IReadOnlyList<InvitationListItemDto>)t.Result, ct);
    }

    public Task<Guid?> FindPendingIdAsync(Guid postId, Guid inviteeUserId, CancellationToken ct)
        => db.invitations.AsNoTracking()
            .Where(i => i.post_id == postId && i.invitee_user_id == inviteeUserId && i.status == "pending")
            .Select(i => (Guid?)i.invitation_id)
            .FirstOrDefaultAsync(ct);

    public Task<(Guid InvitationId, string Status)?> FindAnyAsync(Guid postId, Guid inviteeUserId, CancellationToken ct)
        => db.invitations.AsNoTracking()
            .Where(i => i.post_id == postId && i.invitee_user_id == inviteeUserId)
            .Select(i => new ValueTuple<Guid, string>(i.invitation_id, i.status))
            .FirstOrDefaultAsync(ct)
            .ContinueWith(t => t.Result == default ? (ValueTuple<Guid,string>?)null : t.Result, ct);
}
