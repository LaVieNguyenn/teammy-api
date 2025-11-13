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
            invitationId = dupId;
        }
        else
        {
            // Create new invitation
            invitationId = await repo.CreateAsync(groupId, inviteeUserId, invitedByUserId, message, null, ct);
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
        if (inv.Status != "pending") throw new InvalidOperationException("Invitation already handled");
        var (maxMembers, activeCount) = await groupQueries.GetGroupCapacityAsync(inv.GroupId, ct);
        if (activeCount >= maxMembers) throw new InvalidOperationException("Group is full");

        var hasActive = await groupQueries.HasActiveMembershipInSemesterAsync(currentUserId, inv.SemesterId, ct);
        if (hasActive) throw new InvalidOperationException("User already has active/pending membership in this semester");

        // If user has a pending join-request to this group, promote it; else add new membership
        var pendings = await groupQueries.GetPendingJoinRequestsAsync(inv.GroupId, ct);
        var existingJoin = pendings.FirstOrDefault(x => x.UserId == currentUserId);
        if (existingJoin is not null)
            await groupRepo.UpdateMembershipStatusAsync(existingJoin.RequestId, "member", ct);
        else
            await groupRepo.AddMembershipAsync(inv.GroupId, currentUserId, inv.SemesterId, "member", ct);
        await repo.UpdateStatusAsync(invitationId, "accepted", DateTime.UtcNow, ct);

        // Cleanup: reject other pending applications by this user to the same group
        await postRepo.RejectPendingApplicationsForUserInGroupAsync(inv.GroupId, currentUserId, ct);

        // If group became full, mark open posts as full
        var (maxMembersAfter, activeCountAfter) = await groupQueries.GetGroupCapacityAsync(inv.GroupId, ct);
        if (activeCountAfter >= maxMembersAfter)
        {
            await postRepo.SetOpenPostsStatusForGroupAsync(inv.GroupId, "full", ct);
        }
    }

    public async Task DeclineAsync(Guid invitationId, Guid currentUserId, CancellationToken ct)
    {
        var inv = await queries.GetAsync(invitationId, ct) ?? throw new KeyNotFoundException("Invitation not found");
        if (inv.InviteeUserId != currentUserId) throw new UnauthorizedAccessException("Not your invitation");
        if (inv.Status != "pending") throw new InvalidOperationException("Invitation already handled");

        await repo.UpdateStatusAsync(invitationId, "rejected", DateTime.UtcNow, ct);
    }

    // Leader-only cancel an invitation
    public async Task CancelAsync(Guid invitationId, Guid currentUserId, CancellationToken ct)
    {
        var inv = await queries.GetAsync(invitationId, ct) ?? throw new KeyNotFoundException("Invitation not found");
        var isLeader = await groupQueries.IsLeaderAsync(inv.GroupId, currentUserId, ct);
        if (!isLeader) throw new UnauthorizedAccessException("Leader only");
        if (inv.Status != "pending") throw new InvalidOperationException("Invitation already handled");
        await repo.UpdateStatusAsync(invitationId, "rejected", DateTime.UtcNow, ct);
    }

    public Task<IReadOnlyList<InvitationListItemDto>> ListMyInvitationsAsync(Guid currentUserId, string? status, CancellationToken ct)
        => queries.ListForUserAsync(currentUserId, status, ct);
}
