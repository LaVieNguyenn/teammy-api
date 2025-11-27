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
    IAppUrlProvider urlProvider,
    ITopicReadOnlyQueries topicQueries,
    ITopicWriteRepository topicWrite
)
{
    private readonly ITopicReadOnlyQueries _topicQueries = topicQueries;
    private readonly ITopicWriteRepository _topicWrite = topicWrite;

    public async Task<(Guid InvitationId, bool EmailSent)> InviteUserAsync(Guid groupId, Guid inviteeUserId, Guid invitedByUserId, string? message, CancellationToken ct)
    {
        var isLeader = await groupQueries.IsLeaderAsync(groupId, invitedByUserId, ct);
        if (!isLeader) throw new UnauthorizedAccessException("Leader only");
        var (maxMembers, activeCount) = await groupQueries.GetGroupCapacityAsync(groupId, ct);
        if (activeCount >= maxMembers) throw new InvalidOperationException("Group is full");
        var g = await groupQueries.GetGroupAsync(groupId, ct) ?? throw new KeyNotFoundException("Group not found");
        var hasActive = await groupQueries.HasActiveMembershipInSemesterAsync(inviteeUserId, g.SemesterId, ct);
        if (hasActive) throw new InvalidOperationException("User already has active/pending membership in this semester");
        var now = DateTime.UtcNow;
        var expiresAt = now.AddDays(7);
        var existingAny = await queries.FindAnyAsync(groupId, inviteeUserId, ct);
        Guid invitationId;
        if (existingAny.HasValue)
        {
            var (dupId, _, topicId) = existingAny.Value;
            if (topicId is not null)
                throw new InvalidOperationException("invite_exists_mentor");
            await repo.ResetPendingAsync(dupId, now, expiresAt, ct);
            invitationId = dupId;
        }
        else
        {
            invitationId = await repo.CreateAsync(groupId, inviteeUserId, invitedByUserId, message, expiresAt, null, ct);
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

    public async Task<Guid> InviteMentorAsync(Guid groupId, Guid topicId, Guid mentorUserId, Guid invitedByUserId, string? message, CancellationToken ct)
    {
        var isLeader = await groupQueries.IsLeaderAsync(groupId, invitedByUserId, ct);
        if (!isLeader) throw new UnauthorizedAccessException("Leader only");

        var detail = await groupQueries.GetGroupAsync(groupId, ct) ?? throw new KeyNotFoundException("Group not found");
        var (maxMembers, activeCount) = await groupQueries.GetGroupCapacityAsync(groupId, ct);
        if (activeCount < maxMembers)
            throw new InvalidOperationException("Group must be full before inviting mentor");
        if (string.Equals(detail.Status, "active", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Group is already active");
        if (detail.TopicId.HasValue && detail.TopicId.Value != topicId)
            throw new InvalidOperationException("Group already linked to a different topic");

        var topic = await _topicQueries.GetByIdAsync(topicId, ct) ?? throw new KeyNotFoundException("Topic not found");
        if (!string.Equals(topic.Status, "open", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Topic is not open");
        if (topic.SemesterId != detail.SemesterId)
            throw new InvalidOperationException("Topic must belong to the same semester");
        if (topic.Mentors.All(m => m.MentorId != mentorUserId))
            throw new InvalidOperationException("Mentor is not assigned to this topic");

        var now = DateTime.UtcNow;
        var expiresAt = now.AddDays(7);
        var existingAny = await queries.FindAnyAsync(groupId, mentorUserId, ct);
        Guid invitationId;
        if (existingAny.HasValue)
        {
            var (dupId, status, existingTopicId) = existingAny.Value;
            if (existingTopicId is null)
                throw new InvalidOperationException("invite_exists_member");
            if (existingTopicId != topicId)
                throw new InvalidOperationException("invite_exists_other_topic");
            if (status != "pending")
                throw new InvalidOperationException($"invite_exists:{dupId}:{status}");
            await repo.ResetPendingAsync(dupId, now, expiresAt, ct);
            invitationId = dupId;
        }
        else
        {
            invitationId = await repo.CreateAsync(groupId, mentorUserId, invitedByUserId, message, expiresAt, topicId, ct);
        }

        var mentor = await userQueries.GetCurrentUserAsync(mentorUserId, ct);
        if (!string.IsNullOrWhiteSpace(mentor?.Email))
        {
            var leader = await userQueries.GetCurrentUserAsync(invitedByUserId, ct);
            var actionUrl = urlProvider.GetInvitationUrl(invitationId, groupId);
            var topicDetail = await _topicQueries.GetByIdAsync(topicId, ct);
            var topicTitle = topicDetail?.Title ?? topic.Title;
            var topicDesc = topicDetail?.Description ?? topic.Description;
            var (subject, html) = Teammy.Application.Invitations.Templates.InvitationEmailTemplate.BuildMentorInvite(
                "TEAMMY",
                leader?.DisplayName ?? "Group leader",
                leader?.Email ?? string.Empty,
                detail.Name,
                topicTitle,
                topicDesc,
                actionUrl,
                message,
                logoUrl: null,
                brandHex: "#2563EB"
            );
            await emailSender.SendAsync(
                mentor.Email,
                subject,
                html,
                ct,
                replyToEmail: leader?.Email,
                fromDisplayName: leader?.DisplayName);
        }

        return invitationId;
    }

    public async Task AcceptAsync(Guid invitationId, Guid currentUserId, CancellationToken ct)
    {
        var inv = await queries.GetAsync(invitationId, ct) ?? throw new KeyNotFoundException("Invitation not found");
        if (inv.InviteeUserId != currentUserId) throw new UnauthorizedAccessException("Not your invitation");
        await EnsureInvitationActiveAsync(invitationId, inv, ct);
        if (inv.Status != "pending") throw new InvalidOperationException("Invitation already handled");

        if (inv.TopicId.HasValue)
        {
            await AcceptMentorInvitationAsync(inv, ct);
            return;
        }

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
    private async Task AcceptMentorInvitationAsync(InvitationDetailDto inv, CancellationToken ct)
    {
        if (!inv.TopicId.HasValue)
            throw new InvalidOperationException("Invalid mentor invitation");

        var group = await groupQueries.GetGroupAsync(inv.GroupId, ct) ?? throw new KeyNotFoundException("Group not found");
        if (group.TopicId.HasValue && group.TopicId.Value != inv.TopicId.Value)
            throw new InvalidOperationException("Group already linked to another topic");
        if (string.Equals(group.Status, "active", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Group already active");

        var topic = await _topicQueries.GetByIdAsync(inv.TopicId.Value, ct) ?? throw new KeyNotFoundException("Topic not found");
        if (!string.Equals(topic.Status, "open", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Topic already closed");
        if (topic.SemesterId != group.SemesterId)
            throw new InvalidOperationException("Topic semester mismatch");
        if (topic.Mentors.All(m => m.MentorId != inv.InviteeUserId))
            throw new InvalidOperationException("You are not assigned as mentor for this topic");

        await groupRepo.UpdateGroupAsync(inv.GroupId, null, null, null, null, inv.TopicId, inv.InviteeUserId, ct);
        await groupRepo.SetStatusAsync(inv.GroupId, "active", ct);
        await _topicWrite.SetStatusAsync(inv.TopicId.Value, "closed", ct);
        await postRepo.CloseAllOpenPostsForGroupAsync(inv.GroupId, ct);
        await repo.UpdateStatusAsync(inv.InvitationId, "accepted", DateTime.UtcNow, ct);
        await repo.RevokePendingMentorInvitesAsync(inv.GroupId, inv.InvitationId, ct);
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
