using Teammy.Application.Common.Interfaces;
using Teammy.Application.Invitations.Dtos;
using Teammy.Application.Posts.Dtos;

namespace Teammy.Application.Invitations.Services;

public sealed class InvitationService(
    IGroupReadOnlyQueries groupQueries,
    IGroupRepository groupRepo,
    IRecruitmentPostReadOnlyQueries postQueries,
    IRecruitmentPostRepository postRepo,
    IInvitationRepository repo,
    IInvitationReadOnlyQueries queries,
    IEmailSender emailSender,
    IUserReadOnlyQueries userQueries,
    IAppUrlProvider urlProvider
)
{
    public async Task<(Guid InvitationId, bool EmailSent)> InviteUserAsync(Guid groupId, Guid inviteeUserId, Guid invitedByUserId, string? message, CancellationToken ct)
    {
        // Ensure leader and capacity
        var isLeader = await groupQueries.IsLeaderAsync(groupId, invitedByUserId, ct);
        if (!isLeader) throw new UnauthorizedAccessException("Leader only");

        var (maxMembers, activeCount) = await groupQueries.GetGroupCapacityAsync(groupId, ct);
        if (activeCount >= maxMembers) throw new InvalidOperationException("Group is full");

        // Get group details
        var g = await groupQueries.GetGroupAsync(groupId, ct) ?? throw new KeyNotFoundException("Group not found");

        // Enforce one membership per semester
        var hasActive = await groupQueries.HasActiveMembershipInSemesterAsync(inviteeUserId, g.SemesterId, ct);
        if (hasActive) throw new InvalidOperationException("User already has active/pending membership in this semester");

        var expiresAt = DateTime.UtcNow.AddMinutes(5);

        // Handle duplicate by reusing/reactivating existing invitation (group-based)
        var existingAny = await queries.FindAnyAsync(groupId, inviteeUserId, ct);
        Guid invitationId;
        if (existingAny.HasValue)
        {
            var (dupId, dupStatus) = existingAny.Value;
            if (!string.Equals(dupStatus, "pending", StringComparison.OrdinalIgnoreCase))
            {
                await repo.UpdateStatusAsync(dupId, "pending", null, ct);
            }
            await repo.UpdateExpirationAsync(dupId, expiresAt, ct);
            invitationId = dupId;
        }
        else
        {
            // Create new invitation
            invitationId = await repo.CreateAsync(groupId, inviteeUserId, invitedByUserId, message, expiresAt, ct);
        }

        var invitee = await queries.GetAsync(invitationId, ct);
        bool emailSent = false;
        if (invitee?.InviteeEmail is not null)
        {
            var leader = await userQueries.GetCurrentUserAsync(invitedByUserId, ct);
            var replyTo = leader?.Email;
            var fromDisplayName = leader?.DisplayName;
            var leaderName = string.IsNullOrWhiteSpace(fromDisplayName) ? "Group leader" : fromDisplayName;
            var actionUrl = urlProvider.GetInvitationUrl(invitationId, groupId);
            var (subject, html) = Teammy.Application.Invitations.Templates.InvitationEmailTemplate.Build(
                appName: "TEAMMY",
                leaderName: leaderName!,
                leaderEmail: replyTo ?? string.Empty,
                groupName: invitee.GroupName ?? "Group",
                actionUrl: actionUrl,
                logoUrl: null,
                brandHex: "#F97316");
            emailSent = await emailSender.SendAsync(invitee.InviteeEmail, subject, html, ct, replyToEmail: replyTo, fromDisplayName: fromDisplayName);
        }

        return (invitationId, emailSent);
    }

    public async Task AcceptAsync(Guid invitationId, Guid currentUserId, CancellationToken ct)
    {
        var inv = await queries.GetAsync(invitationId, ct) ?? throw new KeyNotFoundException("Invitation not found");
        if (inv.InviteeUserId != currentUserId) throw new UnauthorizedAccessException("Not your invitation");
        await EnsureInvitationActiveAsync(invitationId, inv, ct);
        if (inv.Status != "pending") throw new InvalidOperationException("Invitation already handled");
        var (maxMembers, activeCount) = await groupQueries.GetGroupCapacityAsync(inv.GroupId, ct);
        if (activeCount >= maxMembers) throw new InvalidOperationException("Group is full");

        var hasActive = await groupQueries.HasActiveMembershipInSemesterAsync(currentUserId, inv.SemesterId, ct);
        if (hasActive) throw new InvalidOperationException("User already has active/pending membership in this semester");

        await groupRepo.AddMembershipAsync(inv.GroupId, currentUserId, inv.SemesterId, "member", ct);
        await repo.UpdateStatusAsync(invitationId, "accepted", DateTime.UtcNow, ct);

        // Cleanup: reject other pending applications by this user to the same group
        await postRepo.RejectPendingApplicationsForUserInGroupAsync(inv.GroupId, currentUserId, ct);

        // Posts stay open until leader selects topic (handled in group update)
    }

    public async Task DeclineAsync(Guid invitationId, Guid currentUserId, CancellationToken ct)
    {
        var inv = await queries.GetAsync(invitationId, ct) ?? throw new KeyNotFoundException("Invitation not found");
        if (inv.InviteeUserId != currentUserId) throw new UnauthorizedAccessException("Not your invitation");
        await EnsureInvitationActiveAsync(invitationId, inv, ct);
        if (inv.Status != "pending") throw new InvalidOperationException("Invitation already handled");

        await repo.UpdateStatusAsync(invitationId, "rejected", DateTime.UtcNow, ct);
    }

    // Leader-only cancel an invitation
    public async Task CancelAsync(Guid invitationId, Guid currentUserId, CancellationToken ct)
    {
        var inv = await queries.GetAsync(invitationId, ct) ?? throw new KeyNotFoundException("Invitation not found");
        var isLeader = await groupQueries.IsLeaderAsync(inv.GroupId, currentUserId, ct);
        if (!isLeader) throw new UnauthorizedAccessException("Leader only");
        await EnsureInvitationActiveAsync(invitationId, inv, ct);
        if (inv.Status != "pending") throw new InvalidOperationException("Invitation already handled");
        await repo.UpdateStatusAsync(invitationId, "revoked", DateTime.UtcNow, ct);
    }

    public async Task<IReadOnlyList<InvitationListItemDto>> ListMyInvitationsAsync(Guid currentUserId, string? status, CancellationToken ct)
    {
        await repo.ExpirePendingAsync(DateTime.UtcNow, ct);
        return await queries.ListForUserAsync(currentUserId, status, ct);
    }

    private async Task EnsureInvitationActiveAsync(Guid invitationId, InvitationDetailDto inv, CancellationToken ct)
    {
        if (inv.ExpiresAt.HasValue && inv.ExpiresAt.Value <= DateTime.UtcNow)
        {
            await repo.UpdateStatusAsync(invitationId, "expired", DateTime.UtcNow, ct);
            throw new InvalidOperationException("Invitation expired");
        }
    }
}
