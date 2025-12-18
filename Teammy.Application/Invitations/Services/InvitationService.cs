using Teammy.Application.Activity.Dtos;
using Teammy.Application.Activity.Services;
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
    ITopicWriteRepository topicWrite,
    IInvitationNotifier invitationNotifier,
    ActivityLogService activityLogService
)
{
    private const string AppName = "TEAMMY";
    private readonly ITopicReadOnlyQueries _topicQueries = topicQueries;
    private readonly ITopicWriteRepository _topicWrite = topicWrite;
    private readonly IInvitationNotifier _invitationNotifier = invitationNotifier;
    private readonly ActivityLogService _activityLog = activityLogService;

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

        await BroadcastInvitationCreatedAsync(invitee, invitationId, inviteeUserId, groupId, "member", invitedByUserId, null, ct);
        await _activityLog.LogAsync(new ActivityLogCreateRequest(invitedByUserId, "group", "GROUP_MEMBER_INVITED")
        {
            GroupId = groupId,
            EntityId = groupId,
            TargetUserId = inviteeUserId,
            Message = $"Leader invited user {inviteeUserId}",
            Metadata = new { invitationId }
        }, ct);

        return (invitationId, emailSent);
    }

    public async Task<Guid> InviteMentorAsync(Guid groupId, Guid topicId, Guid mentorUserId, Guid invitedByUserId, string? message, CancellationToken ct)
    {
        var isLeader = await groupQueries.IsLeaderAsync(groupId, invitedByUserId, ct);
        if (!isLeader) throw new UnauthorizedAccessException("Leader only");

        var detail = await groupQueries.GetGroupAsync(groupId, ct) ?? throw new KeyNotFoundException("Group not found");
        if (string.Equals(detail.Status, "active", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Group is already active");
        if (detail.TopicId.HasValue && detail.TopicId.Value != topicId)
            throw new InvalidOperationException("Group already assigned topic");
        var pendingTopicId = await queries.GetPendingMentorTopicAsync(groupId, ct);
        if (pendingTopicId.HasValue && pendingTopicId.Value != topicId)
            throw new InvalidOperationException("Group already has a pending mentor invitation for another topic");
        var topic = await _topicQueries.GetByIdAsync(topicId, ct) ?? throw new KeyNotFoundException("Topic not found");
        var topicStatus = topic.Status?.ToLowerInvariant();
        if (!string.Equals(topicStatus, "open", StringComparison.OrdinalIgnoreCase))
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

            if (existingTopicId == topicId)
            {
                if (status != "pending")
                    throw new InvalidOperationException($"Invite existed!!!");

                await repo.ResetPendingAsync(dupId, now, expiresAt, ct);
                invitationId = dupId;
            }
            else
            {
                if (status == "pending")
                    throw new InvalidOperationException("Invite pending other topic");

                invitationId = await repo.CreateAsync(groupId, mentorUserId, invitedByUserId, message, expiresAt, topicId, ct);
            }
        }
        else
        {
            invitationId = await repo.CreateAsync(groupId, mentorUserId, invitedByUserId, message, expiresAt, topicId, ct);
        }

        var detailDto = await queries.GetAsync(invitationId, ct);
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

        await BroadcastInvitationCreatedAsync(detailDto, invitationId, mentorUserId, groupId, "mentor", invitedByUserId, topicId, ct);

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
        await postRepo.DeleteProfilePostsForUserAsync(currentUserId, inv.SemesterId, ct);
        await _activityLog.LogAsync(new ActivityLogCreateRequest(currentUserId, "group", "GROUP_MEMBER_JOINED")
        {
            GroupId = inv.GroupId,
            EntityId = inv.GroupId,
            TargetUserId = currentUserId,
            Message = $"User {currentUserId} joined group via invitation",
            Metadata = new { invitationId }
        }, ct);
        await repo.UpdateStatusAsync(invitationId, "accepted", DateTime.UtcNow, ct);
        await BroadcastStatusAsync(inv.InviteeUserId, inv.GroupId, invitationId, "accepted", ct);
        await NotifyInviterAsync(inv, "accepted", ct);

        await postRepo.RejectPendingApplicationsForUserInGroupAsync(inv.GroupId, currentUserId, ct);
    }

    public async Task DeclineAsync(Guid invitationId, Guid currentUserId, CancellationToken ct)
    {
        var inv = await queries.GetAsync(invitationId, ct) ?? throw new KeyNotFoundException("Invitation not found");
        if (inv.InviteeUserId != currentUserId) throw new UnauthorizedAccessException("Not your invitation");
        await EnsureInvitationActiveAsync(invitationId, inv, ct);
        if (inv.Status != "pending") throw new InvalidOperationException("Invitation already handled");

        await repo.UpdateStatusAsync(invitationId, "rejected", DateTime.UtcNow, ct);
        await BroadcastStatusAsync(inv.InviteeUserId, inv.GroupId, invitationId, "rejected", ct);
        await NotifyInviterAsync(inv, "rejected", ct);
    }
    public async Task CancelAsync(Guid invitationId, Guid currentUserId, CancellationToken ct)
    {
        var inv = await queries.GetAsync(invitationId, ct) ?? throw new KeyNotFoundException("Invitation not found");
        var isLeader = await groupQueries.IsLeaderAsync(inv.GroupId, currentUserId, ct);
        if (!isLeader) throw new UnauthorizedAccessException("Leader only");
        await EnsureInvitationActiveAsync(invitationId, inv, ct);
        if (inv.Status != "pending") throw new InvalidOperationException("Invitation already handled");
        await repo.UpdateStatusAsync(invitationId, "revoked", DateTime.UtcNow, ct);
        await BroadcastStatusAsync(inv.InviteeUserId, inv.GroupId, invitationId, "revoked", ct);
    }

    public async Task<IReadOnlyList<InvitationListItemDto>> ListMyInvitationsAsync(Guid currentUserId, string? status, CancellationToken ct)
    {
        _ = await repo.ExpirePendingAsync(DateTime.UtcNow, ct);
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
        var topicStatus = topic.Status?.ToLowerInvariant();
        if (!string.Equals(topicStatus, "open", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Topic already closed");
        if (topic.SemesterId != group.SemesterId)
            throw new InvalidOperationException("Topic semester mismatch");
        if (topic.Mentors.All(m => m.MentorId != inv.InviteeUserId))
            throw new InvalidOperationException("You are not assigned as mentor for this topic");
        var (maxMembers, activeCount) = await groupQueries.GetGroupCapacityAsync(inv.GroupId, ct);
        await groupRepo.UpdateGroupAsync(inv.GroupId, null, null, null, null, inv.TopicId, inv.InviteeUserId, null, ct);
        await _topicWrite.SetStatusAsync(inv.TopicId.Value, "closed", ct);
        if (activeCount >= maxMembers)
        {
            await postRepo.CloseAllOpenPostsForGroupAsync(inv.GroupId, ct);
        }
        await repo.UpdateStatusAsync(inv.InvitationId, "accepted", DateTime.UtcNow, ct);
        await repo.RevokePendingMentorInvitesAsync(inv.GroupId, inv.InvitationId, ct);
        await BroadcastStatusAsync(inv.InviteeUserId, inv.GroupId, inv.InvitationId, "accepted", ct);
        await NotifyInviterAsync(inv, "accepted", ct);

        var rejectedSameTopic = await repo.RejectPendingMentorInvitesForTopicAsync(inv.TopicId.Value, inv.InvitationId, ct);
        foreach (var (invitationId, inviteeUserId, groupId, invitedByUserId) in rejectedSameTopic)
        {
            await BroadcastStatusAsync(inviteeUserId, groupId, invitationId, "rejected", ct);
            var detail = await queries.GetAsync(invitationId, ct);
            if (detail is not null)
            {
                await NotifyTopicTakenAsync(detail, ct);
            }
        }
    }
    private async Task EnsureInvitationActiveAsync(Guid invitationId, InvitationDetailDto inv, CancellationToken ct)
    {
        if (inv.ExpiresAt.HasValue && inv.ExpiresAt.Value <= DateTime.UtcNow)
        {
            await repo.UpdateStatusAsync(invitationId, "expired", DateTime.UtcNow, ct);
            throw new InvalidOperationException("Invitation expired");
        }
    }
    private async Task BroadcastInvitationCreatedAsync(InvitationDetailDto? detail, Guid invitationId, Guid inviteeUserId, Guid groupId, string type, Guid invitedBy, Guid? topicId, CancellationToken ct)
    {
        var dto = detail is not null
            ? ToRealtimeDto(detail)
            : new InvitationRealtimeDto(invitationId, groupId, null, type, "pending", DateTime.UtcNow, invitedBy, topicId, null);

        await _invitationNotifier.NotifyInvitationCreatedAsync(detail?.InviteeUserId ?? inviteeUserId, dto, ct);
        await _invitationNotifier.NotifyGroupPendingAsync(groupId, ct);
    }
    private Task BroadcastStatusAsync(Guid inviteeUserId, Guid groupId, Guid invitationId, string status, CancellationToken ct)
        => Task.WhenAll(
            _invitationNotifier.NotifyInvitationStatusAsync(inviteeUserId, invitationId, status, ct),
            _invitationNotifier.NotifyGroupPendingAsync(groupId, ct));

    private static InvitationRealtimeDto ToRealtimeDto(InvitationDetailDto detail)
        => new(detail.InvitationId, detail.GroupId, detail.GroupName, detail.Type, detail.Status, detail.CreatedAt, detail.InvitedBy, detail.TopicId, detail.TopicTitle);

    private async Task NotifyTopicTakenAsync(InvitationDetailDto inv, CancellationToken ct)
    {
        if (inv.InvitedBy == Guid.Empty) return;
        var inviter = await userQueries.GetCurrentUserAsync(inv.InvitedBy, ct);
        if (inviter?.Email is null) return;
        var group = await groupQueries.GetGroupAsync(inv.GroupId, ct);
        var groupName = group?.Name ?? "your group";
        var topicTitle = inv.TopicTitle ?? "topic";
        var subject = $"{AppName} - Mentor invitation rejected";
        var html = $@"<!doctype html>
<html><body style=""font-family:Segoe UI,Arial,Helvetica,sans-serif;color:#0f172a"">
<p>Your mentor invitation for topic <strong>{System.Net.WebUtility.HtmlEncode(topicTitle)}</strong> was rejected because another group has already assigned this topic.</p>
<p>You can consider choosing another topic.</p>
</body></html>";
        await emailSender.SendAsync(
            inviter.Email,
            subject,
            html,
            ct,
            fromDisplayName: groupName);
    }

    private async Task NotifyInviterAsync(InvitationDetailDto inv, string status, CancellationToken ct)
    {
        if (inv.InvitedBy == Guid.Empty) return;
        var inviter = await userQueries.GetCurrentUserAsync(inv.InvitedBy, ct);
        if (inviter?.Email is null) return;
        var invitee = await userQueries.GetCurrentUserAsync(inv.InviteeUserId, ct);
        var group = await groupQueries.GetGroupAsync(inv.GroupId, ct);
        var inviteeName = invitee?.DisplayName ?? invitee?.Email ?? "The user";
        var groupName = group?.Name ?? "your group";
        var statusText = status.Equals("accepted", StringComparison.OrdinalIgnoreCase) ? "accepted" : "rejected";
        var subject = $"{AppName} - {inviteeName} {statusText} your invitation";
        var html = $@"<!doctype html>
<html><body style=""font-family:Segoe UI,Arial,Helvetica,sans-serif;color:#0f172a"">
<p>{System.Net.WebUtility.HtmlEncode(inviteeName)} has <strong>{statusText}</strong> the invitation for group <b>{System.Net.WebUtility.HtmlEncode(groupName)}</b>.</p>
<p>You can review group members in TEAMMY.</p>
</body></html>";

        await emailSender.SendAsync(
            inviter.Email,
            subject,
            html,
            ct,
            fromDisplayName: groupName);
    }
}
