using Microsoft.EntityFrameworkCore;
using Teammy.Application.Common.Interfaces;
using Teammy.Infrastructure.Persistence;
using Teammy.Infrastructure.Persistence.Models;

namespace Teammy.Infrastructure.Persistence.Repositories;

public sealed class InvitationRepository(AppDbContext db) : IInvitationRepository
{
    public async Task<Guid> CreateAsync(Guid postId, Guid inviteeUserId, Guid invitedBy, string? message, DateTime? expiresAt, CancellationToken ct)
    {
        var inv = new invitation
        {
            invitation_id = Guid.NewGuid(),
            post_id = postId,
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
}

