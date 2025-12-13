using Microsoft.EntityFrameworkCore;
using Teammy.Application.Common.Interfaces;
using Teammy.Infrastructure.Persistence;
using Teammy.Infrastructure.Persistence.Models;

namespace Teammy.Infrastructure.Persistence.Repositories;

public sealed class InvitationRepository(AppDbContext db) : IInvitationRepository
{
    public async Task<Guid> CreateAsync(Guid groupId, Guid inviteeUserId, Guid invitedBy, string? message, DateTime? expiresAt, Guid? topicId, CancellationToken ct)
    {
        var inv = new invitation
        {
            invitation_id = Guid.NewGuid(),
            group_id = groupId,
            topic_id = topicId,
            invitee_user_id = inviteeUserId,
            invited_by = invitedBy,
            status = "pending",
            message = message,
            created_at = DateTime.UtcNow,
            expires_at = expiresAt
        };
        db.invitations.Add(inv);
        await db.SaveChangesAsync(ct);
        return inv.invitation_id;
    }
    public async Task UpdateStatusAsync(Guid invitationId, string newStatus, DateTime? respondedAt, CancellationToken ct)
    {
        var inv = await db.invitations.FirstOrDefaultAsync(x => x.invitation_id == invitationId, ct)
            ?? throw new KeyNotFoundException("Invitation not found");
        inv.status = newStatus;
        inv.responded_at = respondedAt;
        await db.SaveChangesAsync(ct);
    }

    public async Task UpdateExpirationAsync(Guid invitationId, DateTime expiresAt, CancellationToken ct)
    {
        var inv = await db.invitations.FirstOrDefaultAsync(x => x.invitation_id == invitationId, ct)
            ?? throw new KeyNotFoundException("Invitation not found");
        inv.expires_at = expiresAt;
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<(Guid InvitationId, Guid GroupId, Guid? TopicId)>> ExpirePendingAsync(DateTime utcNow, CancellationToken ct)
    {
        var expired = await db.invitations
            .Where(i => i.status == "pending" && i.expires_at.HasValue && i.expires_at <= utcNow)
            .ToListAsync(ct);
        if (expired.Count == 0) return Array.Empty<(Guid, Guid, Guid?)>();

        foreach (var inv in expired)
        {
            inv.status = "expired";
            inv.responded_at = utcNow;
        }

        await db.SaveChangesAsync(ct);
        return expired.Select(i => (i.invitation_id, i.group_id, (Guid?)i.topic_id)).ToList();
    }

    public async Task ResetPendingAsync(Guid invitationId, DateTime newCreatedAt, DateTime expiresAt, CancellationToken ct)
    {
        var inv = await db.invitations.FirstOrDefaultAsync(x => x.invitation_id == invitationId, ct)
            ?? throw new KeyNotFoundException("Invitation not found");
        inv.status = "pending";
        inv.created_at = newCreatedAt;
        inv.responded_at = null;
        inv.expires_at = expiresAt;
        await db.SaveChangesAsync(ct);
    }

    public async Task<int> RevokePendingMentorInvitesAsync(Guid groupId, Guid exceptInvitationId, CancellationToken ct)
    {
        var items = await db.invitations
            .Where(i => i.group_id == groupId && i.invitation_id != exceptInvitationId && i.topic_id != null && i.status == "pending")
            .ToListAsync(ct);
        if (items.Count == 0) return 0;
        foreach (var inv in items)
        {
            inv.status = "revoked";
            inv.responded_at = DateTime.UtcNow;
        }
        await db.SaveChangesAsync(ct);
        return items.Count;
    }
}
