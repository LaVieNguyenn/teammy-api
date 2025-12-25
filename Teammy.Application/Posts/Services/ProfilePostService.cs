using System.Collections.Generic;
using System.Net;
using Teammy.Application.Common.Email;
using Teammy.Application.Common.Interfaces;
using Teammy.Application.Invitations.Dtos;
using Teammy.Application.Invitations.Templates;
using Teammy.Application.Posts.Dtos;

namespace Teammy.Application.Posts.Services;

public sealed class ProfilePostService(
    IRecruitmentPostRepository repo,
    IRecruitmentPostReadOnlyQueries queries,
    IGroupReadOnlyQueries groupQueries,
    IGroupRepository groupRepo,
    IUserReadOnlyQueries userQueries,
    IEmailSender emailSender,
    IAppUrlProvider urlProvider,
    IInvitationRepository invitationRepo,
    IInvitationNotifier invitationNotifier,
    IStudentSemesterReadOnlyQueries studentSemesterQueries,
    ISemesterReadOnlyQueries semesterQueries)
{
    private readonly IGroupReadOnlyQueries _groupQueries = groupQueries;
    private readonly IRecruitmentPostReadOnlyQueries _queries = queries;
    private readonly IUserReadOnlyQueries _userQueries = userQueries;
    private readonly IEmailSender _emailSender = emailSender;
    private readonly IAppUrlProvider _urlProvider = urlProvider;
    private readonly IInvitationRepository _invitationRepo = invitationRepo;
    private readonly IInvitationNotifier _invitationNotifier = invitationNotifier;
    private readonly IStudentSemesterReadOnlyQueries _studentSemesters = studentSemesterQueries;
    private readonly ISemesterReadOnlyQueries _semesterQueries = semesterQueries;
    private const string AppName = "TEAMMY";

    public async Task<Guid> CreateAsync(Guid currentUserId, CreateProfilePostRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Title)) throw new ArgumentException("Title is required");
        var semesterId = await _studentSemesters.GetCurrentSemesterIdAsync(currentUserId, ct)
            ?? throw new InvalidOperationException("No current semester");
        var policy = await _semesterQueries.GetPolicyAsync(semesterId, ct);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        if (policy is null || today < policy.TeamSelfSelectStart || today > policy.TeamSelfSelectEnd)
            throw new InvalidOperationException("Profile-post time is closed");
        var membership = await groupQueries.CheckUserGroupAsync(currentUserId, semesterId, includePending: false, ct);
        if (membership.HasGroup)
            throw new InvalidOperationException("Members of groups cannot create profile posts");
        var userDetail = await _userQueries.GetAdminDetailAsync(currentUserId, ct)
            ?? throw new InvalidOperationException("User not found");
        var targetMajor = req.MajorId ?? userDetail.MajorId;

        return await repo.CreateRecruitmentPostAsync(
            semesterId,
            postType: "individual",
            groupId: null,
            userId: currentUserId,
            targetMajor,
            req.Title,
            req.Description,
            req.Skills,
            requiredSkillsJson: null,
            applicationDeadline: null,
            ct);
    }

    public Task<ProfilePostDetailDto?> GetAsync(Guid id, ExpandOptions expand, Guid? currentUserId, CancellationToken ct)
        => queries.GetProfilePostAsync(id, expand, currentUserId, ct);

    public Task<IReadOnlyList<ProfilePostSummaryDto>> ListAsync(string? skills, Guid? majorId, string? status, ExpandOptions expand, Guid? currentUserId, CancellationToken ct)
        => queries.ListProfilePostsAsync(skills, majorId, status, expand, currentUserId, ct);

    public async Task UpdateAsync(Guid profilePostId, Guid currentUserId, UpdateProfilePostRequest req, CancellationToken ct)
    {
        var owner = await queries.GetPostOwnerAsync(profilePostId, ct);
        if (owner == default)
            throw new KeyNotFoundException("Profile post not found");
        if (owner.OwnerUserId is null || owner.OwnerUserId.Value != currentUserId)
            throw new UnauthorizedAccessException("Not your profile post");

        string? status = null;
        if (!string.IsNullOrWhiteSpace(req.Status))
        {
            var normalized = req.Status.Trim().ToLowerInvariant();
            if (normalized is not ("open" or "closed" or "expired"))
                throw new InvalidOperationException("Invalid status");
            status = normalized;
        }

        await repo.UpdatePostAsync(
            profilePostId,
            req.Title,
            req.Description,
            req.Skills,
            status,
            requiredSkillsJson: null,
            applicationDeadline: null,
            ct);
    }

    public async Task DeleteAsync(Guid profilePostId, Guid currentUserId, CancellationToken ct)
    {
        var owner = await queries.GetPostOwnerAsync(profilePostId, ct);
        if (owner == default)
            throw new KeyNotFoundException("Profile post not found");
        if (owner.OwnerUserId is null || owner.OwnerUserId.Value != currentUserId)
            throw new UnauthorizedAccessException("Not your profile post");
        await repo.DeletePostAsync(profilePostId, ct);
    }

   public async Task InviteAsync(Guid profilePostId, Guid currentUserId, CancellationToken ct)
    {
        var owner = await queries.GetPostOwnerAsync(profilePostId, ct);
        if (owner.OwnerUserId is null)
            throw new InvalidOperationException("Not a profile post");

        if (!string.Equals(owner.Status, "open", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Profile post is not open");

        if (owner.ApplicationDeadline.HasValue && owner.ApplicationDeadline.Value <= DateTime.UtcNow)
        {
            await repo.UpdatePostAsync(profilePostId, null, null, null, "expired", null, null, ct);
            throw new InvalidOperationException("Profile post expired");
        }

        var groupId = await groupQueries.GetLeaderGroupIdAsync(currentUserId, owner.SemesterId, ct)
            ?? throw new UnauthorizedAccessException("Leader group not found in this semester");

        var groupDetail = await groupQueries.GetGroupAsync(groupId, ct) ?? throw new KeyNotFoundException("Group not found");
        var (maxMembers, activeCount) = await groupQueries.GetGroupCapacityAsync(groupId, ct);
        if (activeCount >= maxMembers)
            throw new InvalidOperationException("Group is full");

        var hasActive = await groupQueries.HasActiveMembershipInSemesterAsync(
            owner.OwnerUserId.Value,
            owner.SemesterId,
            ct);

        if (hasActive)
            throw new InvalidOperationException("User already has active/pending membership in this semester");

        var ownerDetail = await _userQueries.GetAdminDetailAsync(owner.OwnerUserId.Value, ct)
            ?? throw new InvalidOperationException("User profile not found");
        var studentMajorId = ownerDetail.MajorId;
        if (groupDetail.MajorId.HasValue)
        {
            if (!studentMajorId.HasValue || studentMajorId.Value != groupDetail.MajorId.Value)
                throw new InvalidOperationException("major_mismatch");
        }

        var existing = await queries.FindApplicationByPostAndGroupAsync(profilePostId, groupId, ct);
        var inviteeEmail = ownerDetail.Email;
        if (existing.HasValue)
        {
            var (applicationId, status) = existing.Value;
            if (string.Equals(status, "pending", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Already Invited!!!");

            await repo.ReactivateApplicationAsync(applicationId, null, ct);
            await SendProfileInviteEmailAsync(profilePostId, groupId, currentUserId, groupDetail.Name, inviteeEmail, ct);
            await BroadcastProfileInvitationAsync(owner.OwnerUserId.Value, groupId, groupDetail.Name, currentUserId, applicationId, ct);
            return;
        }
        var candidateId = await repo.CreateApplicationAsync(
            profilePostId,
            applicantUserId: null,
            applicantGroupId: groupId,
            appliedByUserId: currentUserId,
            message: null,
            ct);
        await SendProfileInviteEmailAsync(profilePostId, groupId, currentUserId, groupDetail.Name, inviteeEmail, ct);
        await BroadcastProfileInvitationAsync(owner.OwnerUserId.Value, groupId, groupDetail.Name, currentUserId, candidateId, ct);
    }

    public Task<IReadOnlyList<ProfilePostInvitationDto>> ListInvitationsAsync(Guid currentUserId, string? status, CancellationToken ct)
        => queries.ListProfileInvitationsAsync(currentUserId, status, ct);

    public async Task AcceptInvitationAsync(Guid postId, Guid candidateId, Guid currentUserId, CancellationToken ct)
    {
        var invitation = await queries.GetProfileInvitationAsync(candidateId, currentUserId, ct)
            ?? throw new KeyNotFoundException("Invitation not found");
        if (invitation.PostId != postId)
            throw new UnauthorizedAccessException("Invitation does not belong to this post");
        if (!string.Equals(invitation.Status, "pending", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Invitation already handled");

        var (maxMembers, activeCount) = await groupQueries.GetGroupCapacityAsync(invitation.GroupId, ct);
        if (activeCount >= maxMembers)
            throw new InvalidOperationException("Group is full");

        var hasActive = await groupQueries.HasActiveMembershipInSemesterAsync(currentUserId, invitation.SemesterId, ct);
        if (hasActive)
            throw new InvalidOperationException("User already has active/pending membership in this semester");

        var userDetail = await _userQueries.GetAdminDetailAsync(currentUserId, ct)
            ?? throw new InvalidOperationException("User profile not found");
        var userMajor = userDetail.MajorId;
        if (invitation.GroupMajorId.HasValue)
        {
            if (!userMajor.HasValue || userMajor.Value != invitation.GroupMajorId.Value)
                throw new InvalidOperationException("major_mismatch");
        }
        await groupRepo.AddMembershipAsync(invitation.GroupId, currentUserId, invitation.SemesterId, "member", ct);
        await repo.UpdateApplicationStatusAsync(candidateId, "accepted", ct);
        await repo.RejectPendingProfileInvitationsAsync(currentUserId, invitation.SemesterId, candidateId, ct);
        await repo.DeleteProfilePostsForUserAsync(currentUserId, invitation.SemesterId, ct);
        await repo.WithdrawPendingApplicationsForUserInSemesterAsync(currentUserId, invitation.SemesterId, ct);
        var revoked = await _invitationRepo.RevokePendingForUserInSemesterAsync(currentUserId, invitation.SemesterId, null, ct);
        foreach (var (revokedId, revokedGroupId) in revoked)
        {
            await _invitationNotifier.NotifyInvitationStatusAsync(currentUserId, revokedId, "revoked", ct);
            await _invitationNotifier.NotifyGroupPendingAsync(revokedGroupId, ct);
        }
        await SendProfileInvitationStatusEmailAsync(invitation, userDetail.DisplayName ?? userDetail.Email ?? "Student", "accepted", ct);
        await _invitationNotifier.NotifyInvitationStatusAsync(currentUserId, candidateId, "accepted", ct);
        await _invitationNotifier.NotifyGroupPendingAsync(invitation.GroupId, ct);
    }

    public async Task RejectInvitationAsync(Guid postId, Guid candidateId, Guid currentUserId, CancellationToken ct)
    {
        var invitation = await queries.GetProfileInvitationAsync(candidateId, currentUserId, ct)
            ?? throw new KeyNotFoundException("Invitation not found");
        if (invitation.PostId != postId)
            throw new UnauthorizedAccessException("Invitation does not belong to this post");
        if (!string.Equals(invitation.Status, "pending", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Invitation already handled");

        await repo.UpdateApplicationStatusAsync(candidateId, "rejected", ct);
        var userDetail = await _userQueries.GetAdminDetailAsync(currentUserId, ct);
        var displayName = userDetail?.DisplayName ?? userDetail?.Email ?? "Student";
        await SendProfileInvitationStatusEmailAsync(invitation, displayName, "rejected", ct);
        await _invitationNotifier.NotifyInvitationStatusAsync(currentUserId, candidateId, "rejected", ct);
        await _invitationNotifier.NotifyGroupPendingAsync(invitation.GroupId, ct);
    }

    private async Task SendProfileInviteEmailAsync(Guid profilePostId, Guid groupId, Guid leaderUserId, string groupName, string? inviteeEmail, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(inviteeEmail)) return;
        var leader = await _userQueries.GetAdminDetailAsync(leaderUserId, ct);
        if (leader is null || string.IsNullOrWhiteSpace(leader.Email)) return;

        var profile = await _queries.GetProfilePostAsync(profilePostId, ExpandOptions.None, null, ct);
        List<(string Label, string Value)>? infoRows = null;
        if (profile is not null)
        {
            infoRows = new List<(string, string)>();
            if (!string.IsNullOrWhiteSpace(profile.Title))
                infoRows.Add(("Profile title", profile.Title));
            if (!string.IsNullOrWhiteSpace(profile.Description))
                infoRows.Add(("Introduction", profile.Description!));
            if (!string.IsNullOrWhiteSpace(profile.Skills))
                infoRows.Add(("Skills", profile.Skills!));
            if (infoRows.Count == 0) infoRows = null;
        }

        var actionUrl = _urlProvider.GetProfilePostUrl(profilePostId);
        var (subject, html) = InvitationEmailTemplate.Build(
            AppName,
            leader.DisplayName ?? "Group leader",
            leader.Email,
            groupName,
            actionUrl,
            logoUrl: null,
            brandHex: "#F97316",
            extraInfo: infoRows,
            extraTitle: "Profile post");

        await _emailSender.SendAsync(
            inviteeEmail!,
            subject,
            html,
            ct,
            replyToEmail: leader.Email,
            fromDisplayName: leader.DisplayName);
    }

    private async Task BroadcastProfileInvitationAsync(Guid inviteeUserId, Guid groupId, string groupName, Guid leaderUserId, Guid candidateId, CancellationToken ct)
    {
        var dto = new InvitationRealtimeDto(
            candidateId,
            groupId,
            groupName,
            "profile_post",
            "pending",
            DateTime.UtcNow,
            leaderUserId,
            null,
            null);

        await _invitationNotifier.NotifyInvitationCreatedAsync(inviteeUserId, dto, ct);
        await _invitationNotifier.NotifyGroupPendingAsync(groupId, ct);
    }

    private async Task SendProfileInvitationStatusEmailAsync(ProfilePostInvitationDetail invitation, string applicantName, string status, CancellationToken ct)
    {
        var leaders = await _groupQueries.ListActiveMembersAsync(invitation.GroupId, ct);
        var leaderRecipients = leaders
            .Where(m => string.Equals(m.Role, "leader", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(m.Email))
            .ToList();
        if (leaderRecipients.Count == 0) return;

        var group = await _groupQueries.GetGroupAsync(invitation.GroupId, ct);
        var groupName = group?.Name ?? "your group";
        var statusText = status.Equals("accepted", StringComparison.OrdinalIgnoreCase) ? "accepted" : "rejected";
        var subject = $"{AppName} - {applicantName} {statusText} your invitation";
        var actionUrl = _urlProvider.GetProfilePostUrl(invitation.PostId);
        var messageHtml = $@"<p>{WebUtility.HtmlEncode(applicantName)} has <strong>{statusText}</strong> the invitation to join <b>{WebUtility.HtmlEncode(groupName)}</b>.</p>";
        var html = EmailTemplateBuilder.Build(
            subject,
            "Profile invitation update",
            messageHtml,
            "View details",
            actionUrl);

        foreach (var leader in leaderRecipients)
        {
            await _emailSender.SendAsync(
                leader.Email,
                subject,
                html,
                ct,
                fromDisplayName: groupName);
        }
    }


}
