using Teammy.Application.Activity.Dtos;
using Teammy.Application.Activity.Services;
using Teammy.Application.Common.Email;
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
    ISemesterReadOnlyQueries semesterQueries,
    IInvitationNotifier invitationNotifier,
    ActivityLogService activityLogService
)
{
    private const string AppName = "TEAMMY";
    private readonly ITopicReadOnlyQueries _topicQueries = topicQueries;
    private readonly ITopicWriteRepository _topicWrite = topicWrite;
    private readonly ISemesterReadOnlyQueries _semesterQueries = semesterQueries;
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

    public async Task<IReadOnlyList<Guid>> InviteMentorAsync(Guid groupId, Guid topicId, Guid mentorUserId, Guid invitedByUserId, string? message, CancellationToken ct)
    {
        var isLeader = await groupQueries.IsLeaderAsync(groupId, invitedByUserId, ct);
        if (!isLeader) throw new UnauthorizedAccessException("Leader only");

        var topic = await _topicQueries.GetByIdAsync(topicId, ct) ?? throw new KeyNotFoundException("Topic not found");
        var mentorIds = topic.Mentors
            .Select(m => m.MentorId)
            .Distinct()
            .ToList();

        if (mentorIds.Count == 0)
            throw new InvalidOperationException("Mentor is not assigned to this topic");

        if (mentorIds.Count == 1)
        {
            if (mentorIds[0] != mentorUserId)
                throw new InvalidOperationException("Mentor is not assigned to this topic");
            var invitationId = await CreateMentorInviteInternalAsync(groupId, topicId, mentorUserId, invitedByUserId, message, invitedByIsMentor: false, ct);
            return new[] { invitationId };
        }

        var invitationIds = new List<Guid>();
        foreach (var mid in mentorIds)
        {
            var invitationId = await CreateMentorInviteInternalAsync(groupId, topicId, mid, invitedByUserId, message, invitedByIsMentor: false, ct);
            if (!invitationIds.Contains(invitationId))
                invitationIds.Add(invitationId);
        }

        return invitationIds;
    }

    public Task<Guid> RequestMentorAsync(Guid groupId, Guid topicId, Guid mentorUserId, string? message, CancellationToken ct)
        => CreateMentorInviteInternalAsync(groupId, topicId, mentorUserId, mentorUserId, message, invitedByIsMentor: true, ct);

    private async Task<Guid> CreateMentorInviteInternalAsync(Guid groupId, Guid topicId, Guid mentorUserId, Guid invitedByUserId, string? message, bool invitedByIsMentor, CancellationToken ct)
    {
        var detail = await groupQueries.GetGroupAsync(groupId, ct) ?? throw new KeyNotFoundException("Group not found");
        if (string.Equals(detail.Status, "active", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Group is already active");

        var policy = await _semesterQueries.GetPolicyAsync(detail.SemesterId, ct);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        if (policy is null || today < policy.TopicSelfSelectStart || today > policy.TopicSelfSelectEnd)
            throw new InvalidOperationException("Topic self-select is closed");

        if (detail.TopicId.HasValue && detail.TopicId.Value != topicId)
            throw new InvalidOperationException("Group already assigned topic");
        var pendingTopicId = await queries.GetPendingMentorTopicAsync(groupId, ct);
        if (pendingTopicId.HasValue && pendingTopicId.Value != topicId)
            throw new InvalidOperationException("Group already has a pending mentor invitation for another topic");
        var topic = await _topicQueries.GetByIdAsync(topicId, ct) ?? throw new KeyNotFoundException("Topic not found");
        var topicStatus = topic.Status?.ToLowerInvariant();
        var isGroupTopic = detail.TopicId.HasValue && detail.TopicId.Value == topicId;
        if (!string.Equals(topicStatus, "open", StringComparison.OrdinalIgnoreCase) && !isGroupTopic)
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
                if (status == "pending")
                {
                    if (invitedByIsMentor)
                    {
                        throw new InvalidOperationException("Invite already sent by leader. Please accept the invitation.");
                    }

                    await repo.ResetPendingAsync(dupId, now, expiresAt, ct);
                    invitationId = dupId;
                }
                else
                {
                    throw new InvalidOperationException("Invite existed!!!");
                }
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
        if (!invitedByIsMentor && !string.IsNullOrWhiteSpace(mentor?.Email))
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

        var invitationType = invitedByIsMentor ? "mentor_request" : "mentor";
        await BroadcastInvitationCreatedAsync(detailDto, invitationId, mentorUserId, groupId, invitationType, invitedByUserId, topicId, ct);

        if (invitedByIsMentor)
        {
            await repo.MarkMentorAwaitingLeaderAsync(invitationId, now, ct);
            var refreshed = await queries.GetAsync(invitationId, ct);
            await BroadcastStatusAsync(mentorUserId, groupId, invitationId, "pending_leader", ct);
            var notifyDetail = refreshed ?? detailDto;
            if (notifyDetail is not null)
                await NotifyLeaderMentorPendingAsync(notifyDetail, ct);
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
        await postRepo.WithdrawPendingApplicationsForUserInSemesterAsync(currentUserId, inv.SemesterId, ct);
        var revoked = await repo.RevokePendingForUserInSemesterAsync(currentUserId, inv.SemesterId, invitationId, ct);
        foreach (var (revokedId, revokedGroupId) in revoked)
        {
            await BroadcastStatusAsync(currentUserId, revokedGroupId, revokedId, "revoked", ct);
        }
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
    public async Task ApproveMentorInvitationAsync(Guid invitationId, Guid leaderUserId, CancellationToken ct)
    {
        var inv = await queries.GetAsync(invitationId, ct) ?? throw new KeyNotFoundException("Invitation not found");
        if (!inv.TopicId.HasValue) throw new InvalidOperationException("Not a mentor invitation");
        var isLeader = await groupQueries.IsLeaderAsync(inv.GroupId, leaderUserId, ct);
        if (!isLeader) throw new UnauthorizedAccessException("Leader only");
        if (inv.InvitedBy != inv.InviteeUserId)
            throw new InvalidOperationException("Leader approval is not required for leader-sent mentor invites");
        await EnsureInvitationActiveAsync(invitationId, inv, ct);
        if (inv.Status != "pending") throw new InvalidOperationException("Invitation already handled");
        if (!inv.RespondedAt.HasValue)
            await repo.MarkMentorAwaitingLeaderAsync(invitationId, DateTime.UtcNow, ct);

        await CompleteMentorAssignmentAsync(inv, ct);
        await NotifyMentorDecisionAsync(inv, approved: true, ct);
    }

    public async Task RejectMentorInvitationAsync(Guid invitationId, Guid leaderUserId, CancellationToken ct)
    {
        var inv = await queries.GetAsync(invitationId, ct) ?? throw new KeyNotFoundException("Invitation not found");
        if (!inv.TopicId.HasValue) throw new InvalidOperationException("Not a mentor invitation");
        var isLeader = await groupQueries.IsLeaderAsync(inv.GroupId, leaderUserId, ct);
        if (!isLeader) throw new UnauthorizedAccessException("Leader only");
        await EnsureInvitationActiveAsync(invitationId, inv, ct);
        if (inv.Status != "pending") throw new InvalidOperationException("Invitation already handled");

        if (!inv.RespondedAt.HasValue)
        {
            await CancelAsync(invitationId, leaderUserId, ct);
            return;
        }

        await repo.UpdateStatusAsync(invitationId, "rejected", DateTime.UtcNow, ct);
        await BroadcastStatusAsync(inv.InviteeUserId, inv.GroupId, invitationId, "rejected", ct);
        await NotifyMentorDecisionAsync(inv, approved: false, ct);
    }

    private async Task AcceptMentorInvitationAsync(InvitationDetailDto inv, CancellationToken ct)
    {
        if (!inv.TopicId.HasValue)
            throw new InvalidOperationException("Invalid mentor invitation");
        if (inv.RespondedAt.HasValue)
            throw new InvalidOperationException("Invitation already handled");

        if (inv.InvitedBy == inv.InviteeUserId)
        {
            await repo.MarkMentorAwaitingLeaderAsync(inv.InvitationId, DateTime.UtcNow, ct);
            await BroadcastStatusAsync(inv.InviteeUserId, inv.GroupId, inv.InvitationId, "pending_leader", ct);
            await NotifyLeaderMentorPendingAsync(inv, ct);
            return;
        }

        await CompleteMentorAssignmentAsync(inv, ct);
    }

    private async Task CompleteMentorAssignmentAsync(InvitationDetailDto inv, CancellationToken ct)
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
        var groupHasTopic = group.TopicId.HasValue && group.TopicId.Value == inv.TopicId.Value;
        if (!string.Equals(topicStatus, "open", StringComparison.OrdinalIgnoreCase) && !groupHasTopic)
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
        await BroadcastStatusAsync(inv.InviteeUserId, inv.GroupId, inv.InvitationId, "accepted", ct);
        await NotifyInviterAsync(inv, "accepted", ct);

        var autoAcceptInvites = await queries.ListPendingMentorInvitesForGroupTopicAsync(inv.GroupId, inv.TopicId.Value, inv.InvitationId, ct);
        foreach (var (otherInvitationId, inviteeUserId, invitedByUserId) in autoAcceptInvites)
        {
            if (invitedByUserId == inviteeUserId)
                continue;

            await groupRepo.UpdateGroupAsync(inv.GroupId, null, null, null, null, null, inviteeUserId, null, ct);
            await repo.UpdateStatusAsync(otherInvitationId, "accepted", DateTime.UtcNow, ct);
            await BroadcastStatusAsync(inviteeUserId, inv.GroupId, otherInvitationId, "accepted", ct);
            var detail = await queries.GetAsync(otherInvitationId, ct);
            if (detail is not null)
            {
                await NotifyInviterAsync(detail, "accepted", ct);
            }
        }

        var rejectedSameTopic = await repo.RejectPendingMentorInvitesForTopicAsync(inv.TopicId.Value, inv.InvitationId, inv.GroupId, ct);
        foreach (var (otherInvitationId, inviteeUserId, groupId, invitedByUserId) in rejectedSameTopic)
        {
            await BroadcastStatusAsync(inviteeUserId, groupId, otherInvitationId, "rejected", ct);
            var detail = await queries.GetAsync(otherInvitationId, ct);
            if (detail is not null)
            {
                await NotifyTopicTakenAsync(detail, ct);
            }
        }
    }

    private async Task NotifyLeaderMentorPendingAsync(InvitationDetailDto inv, CancellationToken ct)
    {
        var leaderId = await groupQueries.GetGroupLeaderUserIdAsync(inv.GroupId, ct);
        if (!leaderId.HasValue && inv.InvitedBy != Guid.Empty)
            leaderId = inv.InvitedBy;
        if (!leaderId.HasValue) return;
        var leader = await userQueries.GetCurrentUserAsync(leaderId.Value, ct);
        if (leader?.Email is null) return;
        var mentor = await userQueries.GetCurrentUserAsync(inv.InviteeUserId, ct);
        var group = await groupQueries.GetGroupAsync(inv.GroupId, ct);
        var mentorName = mentor?.DisplayName ?? mentor?.Email ?? "A mentor";
        var groupName = group?.Name ?? "your group";
        var topicTitle = inv.TopicTitle ?? "topic";
        var actionUrl = urlProvider.GetInvitationUrl(inv.InvitationId, inv.GroupId);
        var subject = $"{AppName} - {mentorName} accepted the mentor invitation";
        var messageHtml = $@"<p><strong>{System.Net.WebUtility.HtmlEncode(mentorName)}</strong> agreed to mentor <strong>{System.Net.WebUtility.HtmlEncode(groupName)}</strong> for topic <strong>{System.Net.WebUtility.HtmlEncode(topicTitle)}</strong>.</p>
<p style=""margin-top:8px;color:#475569;"">Please review and approve or reject this mentor in TEAMMY.</p>";
        var html = EmailTemplateBuilder.Build(
            subject,
            "Mentor waiting for approval",
            messageHtml,
            "Review mentor",
            actionUrl);

        await emailSender.SendAsync(
            leader.Email,
            subject,
            html,
            ct,
            fromDisplayName: groupName);
    }

    private async Task NotifyMentorDecisionAsync(InvitationDetailDto inv, bool approved, CancellationToken ct)
    {
        var mentor = await userQueries.GetCurrentUserAsync(inv.InviteeUserId, ct);
        if (mentor?.Email is null) return;
        var group = await groupQueries.GetGroupAsync(inv.GroupId, ct);
        var groupName = group?.Name ?? "the group";
        var topicTitle = inv.TopicTitle ?? "the topic";
        var decisionText = approved ? "approved" : "rejected";
        var subject = $"{AppName} - Your mentor request was {decisionText}";
        var reason = approved
            ? $"You are now the mentor of <strong>{System.Net.WebUtility.HtmlEncode(groupName)}</strong>."
            : $"The leader of <strong>{System.Net.WebUtility.HtmlEncode(groupName)}</strong> has rejected the mentor request.";
        var actionUrl = urlProvider.GetInvitationUrl(inv.InvitationId, inv.GroupId);
        var messageHtml = $@"<p>{reason}</p>
<p style=""margin-top:8px;"">Topic: <strong>{System.Net.WebUtility.HtmlEncode(topicTitle)}</strong></p>
<p style=""margin-top:8px;color:#475569;"">Please login to TEAMMY for details.</p>";
        var html = EmailTemplateBuilder.Build(
            subject,
            "Mentor decision update",
            messageHtml,
            "Open Teammy",
            actionUrl);

        await emailSender.SendAsync(
            mentor.Email,
            subject,
            html,
            ct,
            fromDisplayName: groupName);
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
        var actionUrl = urlProvider.GetInvitationUrl(inv.InvitationId, inv.GroupId);
        var messageHtml = $@"<p>Your mentor invitation for topic <strong>{System.Net.WebUtility.HtmlEncode(topicTitle)}</strong> was rejected because another group has already assigned this topic.</p>
<p style=""margin-top:8px;color:#475569;"">You can consider choosing another topic.</p>";
        var html = EmailTemplateBuilder.Build(
            subject,
            "Mentor invitation update",
            messageHtml,
            "Open Teammy",
            actionUrl);
        await emailSender.SendAsync(
            inviter.Email,
            subject,
            html,
            ct,
            fromDisplayName: groupName);
    }

    private async Task NotifyInviterAsync(InvitationDetailDto inv, string status, CancellationToken ct)
    {
        if (inv.InvitedBy == Guid.Empty || inv.InvitedBy == inv.InviteeUserId) return;
        var inviter = await userQueries.GetCurrentUserAsync(inv.InvitedBy, ct);
        if (inviter?.Email is null) return;
        var invitee = await userQueries.GetCurrentUserAsync(inv.InviteeUserId, ct);
        var group = await groupQueries.GetGroupAsync(inv.GroupId, ct);
        var inviteeName = invitee?.DisplayName ?? invitee?.Email ?? "The user";
        var groupName = group?.Name ?? "your group";
        var statusText = status.Equals("accepted", StringComparison.OrdinalIgnoreCase) ? "accepted" : "rejected";
        var subject = $"{AppName} - {inviteeName} {statusText} your invitation";
        var actionUrl = urlProvider.GetInvitationUrl(inv.InvitationId, inv.GroupId);
        var messageHtml = $@"<p>{System.Net.WebUtility.HtmlEncode(inviteeName)} has <strong>{statusText}</strong> the invitation for group <b>{System.Net.WebUtility.HtmlEncode(groupName)}</b>.</p>
<p style=""margin-top:8px;color:#475569;"">You can review group members in TEAMMY.</p>";
        var html = EmailTemplateBuilder.Build(
            subject,
            "Invitation status update",
            messageHtml,
            "Open Teammy",
            actionUrl);

        await emailSender.SendAsync(
            inviter.Email,
            subject,
            html,
            ct,
            fromDisplayName: groupName);
    }
}
