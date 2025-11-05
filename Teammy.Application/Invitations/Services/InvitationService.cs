using Teammy.Application.Common.Interfaces;
using Teammy.Application.Invitations.Dtos;

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

        // Find or create recruitment post for the group
        var post = await postQueries.ListAsync(null, g.MajorId, "open", ct);
        var postId = post.FirstOrDefault(p => p.GroupId == groupId)?.Id
                     ?? await postRepo.CreateRecruitmentPostAsync(g.SemesterId, "group_hiring", groupId, null, g.MajorId, $"Invitation for {g.Name}", null, null, ct);

        // Create invitation
        var invitationId = await repo.CreateAsync(postId, inviteeUserId, invitedByUserId, message, null, ct);

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
        if (inv.GroupId is null) throw new InvalidOperationException("Invitation not bound to a group post");

        var (maxMembers, activeCount) = await groupQueries.GetGroupCapacityAsync(inv.GroupId.Value, ct);
        if (activeCount >= maxMembers) throw new InvalidOperationException("Group is full");

        var hasActive = await groupQueries.HasActiveMembershipInSemesterAsync(currentUserId, inv.SemesterId, ct);
        if (hasActive) throw new InvalidOperationException("User already has active/pending membership in this semester");

        await groupRepo.AddMembershipAsync(inv.GroupId.Value, currentUserId, inv.SemesterId, "member", ct);
        await repo.UpdateStatusAsync(invitationId, "accepted", DateTime.UtcNow, ct);
    }

    public async Task DeclineAsync(Guid invitationId, Guid currentUserId, CancellationToken ct)
    {
        var inv = await queries.GetAsync(invitationId, ct) ?? throw new KeyNotFoundException("Invitation not found");
        if (inv.InviteeUserId != currentUserId) throw new UnauthorizedAccessException("Not your invitation");
        if (inv.Status != "pending") throw new InvalidOperationException("Invitation already handled");

        await repo.UpdateStatusAsync(invitationId, "declined", DateTime.UtcNow, ct);
    }

    public Task<IReadOnlyList<InvitationListItemDto>> ListMyInvitationsAsync(Guid currentUserId, string? status, CancellationToken ct)
        => queries.ListForUserAsync(currentUserId, status, ct);
}
