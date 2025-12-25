using System.Collections.Generic;
using System.Linq;
using System.Net;
using Teammy.Application.Common.Email;
using Teammy.Application.Activity.Dtos;
using Teammy.Application.Activity.Services;
using Teammy.Application.Common.Interfaces;
using Teammy.Application.Groups.Dtos;
using Teammy.Application.Semesters.Dtos;

namespace Teammy.Application.Groups.Services;

public sealed class GroupService(
    IGroupRepository repo,
    IGroupReadOnlyQueries queries,
    IUserReadOnlyQueries userQueries,
    IRecruitmentPostRepository postRepo,
    ActivityLogService activityLogService,
    IEmailSender emailSender,
    ISemesterReadOnlyQueries semesterQueries,
    IGroupStatusNotifier groupStatusNotifier)
{
    private const string AppName = "TEAMMY";
    private const string DefaultAppUrl = "https://teammy.vercel.app/login";
    private readonly IUserReadOnlyQueries _userQueries = userQueries;
    private readonly IRecruitmentPostRepository _postRepo = postRepo;
    private readonly ActivityLogService _activityLog = activityLogService;
    private readonly IEmailSender _emailSender = emailSender;
    private readonly ISemesterReadOnlyQueries _semesterQueries = semesterQueries;
    private readonly IGroupStatusNotifier _groupStatusNotifier = groupStatusNotifier;
    private readonly Dictionary<Guid, SemesterPolicyDto?> _semesterPolicyCache = new();
    private readonly Dictionary<Guid, SemesterDetailDto?> _semesterDetailCache = new();

    public async Task<Guid> CreateGroupAsync(Guid creatorUserId, CreateGroupRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Name))
            throw new ArgumentException("Name is required");
        var normalizedName = req.Name.Trim();
        var semesterId = await queries.GetActiveSemesterIdAsync(ct)
            ?? throw new InvalidOperationException("No active semester available");

        var policy = await _semesterQueries.GetPolicyAsync(semesterId, ct);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        if (policy is null || today < policy.TeamSelfSelectStart || today > policy.TeamSelfSelectEnd)
            throw new InvalidOperationException("Team self-select is closed");

        var nameExists = await queries.GroupNameExistsAsync(semesterId, normalizedName, null, ct);
        if (nameExists)
            throw new InvalidOperationException("Group name already exists in this semester");

        var (minSize, maxSize) = await queries.GetGroupSizePolicyAsync(semesterId, ct);
        if (req.MaxMembers < minSize || req.MaxMembers > maxSize)
            throw new ArgumentException($"Members must be between {minSize} and {maxSize} in this semester");

        var hasActive = await queries.HasActiveMembershipInSemesterAsync(creatorUserId, semesterId, ct);
        if (hasActive)
            throw new InvalidOperationException("User already has active/pending membership in this semester");

        var creator = await _userQueries.GetAdminDetailAsync(creatorUserId, ct)
            ?? throw new InvalidOperationException("User not found");
        var majorId = creator.MajorId;
        if (!majorId.HasValue)
            throw new InvalidOperationException("User hasn't major");

        var groupId = await repo.CreateGroupAsync(semesterId, null, majorId, normalizedName, req.Description, req.MaxMembers, null, ct);
        await repo.AddMembershipAsync(groupId, creatorUserId, semesterId, "leader", ct);
        await _postRepo.DeleteProfilePostsForUserAsync(creatorUserId, semesterId, ct);
        await _postRepo.WithdrawPendingApplicationsForUserInSemesterAsync(creatorUserId, semesterId, ct);
        await LogAsync(new ActivityLogCreateRequest(creatorUserId, "group", "GROUP_CREATED")
        {
            GroupId = groupId,
            EntityId = groupId,
            Message = $"{creator.DisplayName ?? creator.Email ?? "Leader"} created group {req.Name}",
            Metadata = new { req.Name, req.MaxMembers }
        }, ct);
        return groupId;
    }

    public async Task<IReadOnlyList<GroupSummaryDto>> ListGroupsAsync(string? status, Guid? majorId, Guid? topicId, CancellationToken ct)
    {
        var list = await queries.ListGroupsAsync(status, majorId, topicId, ct);
        var snapshots = list.Select(x => new ActivationSnapshot(
            x.Id,
            x.Semester.SemesterId,
            x.Status,
            x.Topic is not null,
            x.Mentor is not null,
            x.CurrentMembers,
            x.MaxMembers)).ToList();
        var closed = await AutoCloseExpiredGroupsAsync(snapshots, ct);
        if (closed)
        {
            list = await queries.ListGroupsAsync(status, majorId, topicId, ct);
            snapshots = list.Select(x => new ActivationSnapshot(
                x.Id,
                x.Semester.SemesterId,
                x.Status,
                x.Topic is not null,
                x.Mentor is not null,
                x.CurrentMembers,
                x.MaxMembers)).ToList();
        }
        var activated = await AutoActivateEligibleGroupsAsync(snapshots, ct);
        if (activated)
            list = await queries.ListGroupsAsync(status, majorId, topicId, ct);
        return list;
    }

    public async Task<GroupDetailDto?> GetGroupAsync(Guid id, CancellationToken ct)
    {
        var detail = await queries.GetGroupAsync(id, ct);
        if (detail is null) return null;
        var closeSnapshot = new ActivationSnapshot(
            detail.Id,
            detail.SemesterId,
            detail.Status,
            detail.TopicId.HasValue,
            false,
            detail.CurrentMembers,
            detail.MaxMembers);
        var closed = await MaybeAutoCloseGroupAsync(closeSnapshot, ct);
        if (closed)
        {
            detail = await queries.GetGroupAsync(id, ct);
            if (detail is null) return null;
        }
        var mentor = await queries.GetMentorAsync(id, ct);
        var activated = await AutoActivateGroupAsync(
            new ActivationSnapshot(
                detail.Id,
                detail.SemesterId,
                detail.Status,
                detail.TopicId.HasValue,
                mentor is not null,
                detail.CurrentMembers,
                detail.MaxMembers),
            ct);
        if (activated)
            detail = await queries.GetGroupAsync(id, ct);
        return detail;
    }

    public async Task LeaveGroupAsync(Guid groupId, Guid userId, CancellationToken ct)
    {
        var detail = await queries.GetGroupAsync(groupId, ct) ?? throw new KeyNotFoundException("Group not found");
        if (string.Equals(detail.Status, "active", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Cannot leave an active group");

        var isLeader = await queries.IsLeaderAsync(groupId, userId, ct);
        if (isLeader)
        {
            if (detail.TopicId.HasValue)
            {
                var confirmedMentor = await queries.GetMentorAsync(groupId, ct);
                if (confirmedMentor is not null)
                    throw new InvalidOperationException("Leader cannot leave after mentor confirmation. Transfer leadership first.");
            }
            var (_, activeCount) = await queries.GetGroupCapacityAsync(groupId, ct);
            if (activeCount > 1)
                throw new InvalidOperationException("Change Leaderfirst");
        }

        var ok = await repo.LeaveGroupAsync(groupId, userId, ct);
        if (!ok)
            throw new InvalidOperationException("Not a member of this group");

        var groupStillExists = await queries.GetGroupAsync(groupId, ct) is not null;

        await LogAsync(new ActivityLogCreateRequest(userId, "group", "GROUP_MEMBER_LEFT")
        {
            GroupId = groupStillExists ? groupId : null,
            EntityId = groupId,
            TargetUserId = userId,
            Message = $"User {userId} left group"
        }, ct);

        if (groupStillExists && !isLeader)
        {
            await SendMemberLeftLeaderEmailAsync(groupId, userId, ct);
        }
    }


    public async Task InviteUserAsync(Guid groupId, Guid inviteeUserId, Guid currentUserId, CancellationToken ct)
    {
        var isLeader = await queries.IsLeaderAsync(groupId, currentUserId, ct);
        if (!isLeader) throw new UnauthorizedAccessException("Leader only");

        var detail = await queries.GetGroupAsync(groupId, ct) ?? throw new KeyNotFoundException("Group not found");
        var (maxMembers, activeCount) = await queries.GetGroupCapacityAsync(groupId, ct);
        if (activeCount >= maxMembers)
            throw new InvalidOperationException("Group is full");

        var hasActive = await queries.HasActiveMembershipInSemesterAsync(inviteeUserId, detail.SemesterId, ct);
        if (hasActive)
            throw new InvalidOperationException("User already has active/pending membership in this semester");

        await repo.AddMembershipAsync(groupId, inviteeUserId, detail.SemesterId, "pending", ct);
        await LogAsync(new ActivityLogCreateRequest(currentUserId, "group", "GROUP_MEMBER_INVITED")
        {
            GroupId = groupId,
            EntityId = groupId,
            TargetUserId = inviteeUserId,
            Message = $"Leader invited user {inviteeUserId}",
            Metadata = new { inviteeUserId }
        }, ct);
    }

    public async Task ConfirmActiveAsync(Guid groupId, Guid currentUserId, CancellationToken ct)
    {
        var isLeader = await queries.IsLeaderAsync(groupId, currentUserId, ct);
        if (!isLeader) throw new UnauthorizedAccessException("Leader only");

        var detail = await queries.GetGroupAsync(groupId, ct) ?? throw new KeyNotFoundException("Group not found");
        if (string.Equals(detail.Status, "active", StringComparison.OrdinalIgnoreCase))
            return;

        var readiness = await EvaluateActivationReadinessAsync(detail, ct);
        if (!readiness.Ready)
            throw new InvalidOperationException(readiness.ErrorMessage ?? "Group is not ready to activate");

        await ActivateGroupInternalAsync(groupId, currentUserId, "Leader confirmated!!!", ct);
    }

    public Task<IReadOnlyList<MyGroupDto>> ListMyGroupsAsync(Guid currentUserId, Guid? semesterId, CancellationToken ct)
        => queries.ListMyGroupsAsync(currentUserId, semesterId, ct);

    public Task<IReadOnlyList<GroupMemberDto>> ListActiveMembersAsync(Guid groupId, CancellationToken ct)
        => queries.ListActiveMembersAsync(groupId, ct);

    public async Task RequestCloseGroupAsync(Guid groupId, Guid currentUserId, CancellationToken ct)
    {
        var isLeader = await queries.IsLeaderAsync(groupId, currentUserId, ct);
        if (!isLeader) throw new UnauthorizedAccessException("Leader only");

        var detail = await queries.GetGroupAsync(groupId, ct) ?? throw new KeyNotFoundException("Group not found");
        if (string.Equals(detail.Status, "closed", StringComparison.OrdinalIgnoreCase))
            return;
        if (string.Equals(detail.Status, "pending_close", StringComparison.OrdinalIgnoreCase))
            return;
        if (!string.Equals(detail.Status, "active", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Group must be active to request close");

        var mentor = await queries.GetMentorAsync(groupId, ct);
        if (mentor is null)
            throw new InvalidOperationException("Mentor not assigned to this group");
        if (!detail.TopicId.HasValue)
            throw new InvalidOperationException("Topic not selected for this group");
        var (maxMembers, activeCount) = await queries.GetGroupCapacityAsync(groupId, ct);
        if (activeCount < maxMembers)
            throw new InvalidOperationException($"Need {maxMembers - activeCount} more member before closing");

        await repo.SetStatusAsync(groupId, "pending_close", ct);
        await LogAsync(new ActivityLogCreateRequest(currentUserId, "group", "GROUP_CLOSE_REQUESTED")
        {
            GroupId = groupId,
            EntityId = groupId,
            Message = "Leader requested to close the group"
        }, ct);

        await SendCloseRequestEmailToMentorAsync(groupId, mentor, ct);
        await NotifyCloseStatusAsync(groupId, mentor.UserId, "pending_close", "close_requested", ct);
        await NotifyCloseStatusToLeadersAsync(groupId, "pending_close", "close_requested", ct);
    }

    public async Task ConfirmCloseGroupAsync(Guid groupId, Guid currentUserId, CancellationToken ct)
    {
        var isMentor = await queries.IsMentorAsync(groupId, currentUserId, ct);
        if (!isMentor)
            throw new UnauthorizedAccessException("Mentor only");

        var detail = await queries.GetGroupAsync(groupId, ct) ?? throw new KeyNotFoundException("Group not found");
        if (!string.Equals(detail.Status, "pending_close", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Group is not pending close");

        await repo.CloseGroupAsync(groupId, ct);
        await LogAsync(new ActivityLogCreateRequest(currentUserId, "group", "GROUP_CLOSED_BY_MENTOR")
        {
            GroupId = groupId,
            EntityId = groupId,
            Message = "Mentor confirmed and closed the group"
        }, ct);

        var mentorProfile = await _userQueries.GetAdminDetailAsync(currentUserId, ct);
        var mentor = new GroupMentorDto(
            currentUserId,
            mentorProfile?.Email ?? string.Empty,
            mentorProfile?.DisplayName ?? "Mentor",
            mentorProfile?.AvatarUrl);

        await SendCloseConfirmedEmailToLeadersAsync(groupId, mentor, ct);
        await NotifyCloseStatusAsync(groupId, currentUserId, "closed", "close_confirmed", ct);
        await NotifyCloseStatusToLeadersAsync(groupId, "closed", "close_confirmed", ct);
    }

    public async Task RejectCloseGroupAsync(Guid groupId, Guid currentUserId, CancellationToken ct)
    {
        var isMentor = await queries.IsMentorAsync(groupId, currentUserId, ct);
        if (!isMentor)
            throw new UnauthorizedAccessException("Mentor only");

        var detail = await queries.GetGroupAsync(groupId, ct) ?? throw new KeyNotFoundException("Group not found");
        if (!string.Equals(detail.Status, "pending_close", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Group is not pending close");

        await repo.SetStatusAsync(groupId, "active", ct);
        await LogAsync(new ActivityLogCreateRequest(currentUserId, "group", "GROUP_CLOSE_REJECTED")
        {
            GroupId = groupId,
            EntityId = groupId,
            Message = "Mentor rejected the close request"
        }, ct);

        var mentorProfile = await _userQueries.GetAdminDetailAsync(currentUserId, ct);
        var mentor = new GroupMentorDto(
            currentUserId,
            mentorProfile?.Email ?? string.Empty,
            mentorProfile?.DisplayName ?? "Mentor",
            mentorProfile?.AvatarUrl);

        await SendCloseRejectedEmailToLeadersAsync(groupId, mentor, ct);
        await NotifyCloseStatusAsync(groupId, currentUserId, "active", "close_rejected", ct);
        await NotifyCloseStatusToLeadersAsync(groupId, "active", "close_rejected", ct);
    }

    public async Task CloseGroupAsync(Guid groupId, Guid currentUserId, CancellationToken ct)
    {
        var isLeader = await queries.IsLeaderAsync(groupId, currentUserId, ct);
        if (!isLeader) throw new UnauthorizedAccessException("Leader only");
        await repo.CloseGroupAsync(groupId, ct);
    }

    public async Task TransferLeadershipAsync(Guid groupId, Guid currentUserId, Guid newLeaderUserId, CancellationToken ct)
    {
        var isLeader = await queries.IsLeaderAsync(groupId, currentUserId, ct);
        if (!isLeader) throw new UnauthorizedAccessException("Leader only");
        await repo.TransferLeadershipAsync(groupId, currentUserId, newLeaderUserId, ct);
        await LogAsync(new ActivityLogCreateRequest(currentUserId, "group", "GROUP_LEADER_CHANGED")
        {
            GroupId = groupId,
            EntityId = groupId,
            TargetUserId = newLeaderUserId,
            Metadata = new { previousLeaderId = currentUserId, newLeaderId = newLeaderUserId }
        }, ct);
    }

    public Task<UserGroupCheckDto> CheckUserGroupAsync(Guid targetUserId, Guid? semesterId, bool includePending, CancellationToken ct)
        => queries.CheckUserGroupAsync(targetUserId, semesterId, includePending, ct);

    public async Task UpdateGroupAsync(Guid groupId, Guid currentUserId, UpdateGroupRequest req, CancellationToken ct)
    {
        var detail = await queries.GetGroupAsync(groupId, ct) ?? throw new KeyNotFoundException("Group not found");
        var isLeader = await queries.IsLeaderAsync(groupId, currentUserId, ct);
        if (!isLeader) throw new UnauthorizedAccessException("Leader only");

        if (!string.IsNullOrWhiteSpace(req.Name))
        {
            var exists = await queries.GroupNameExistsAsync(detail.SemesterId, req.Name!, groupId, ct);
            if (exists) throw new InvalidOperationException("Group name already exists in this semester");
        }

        if (req.MaxMembers.HasValue)
        {
            var (_, activeCount) = await queries.GetGroupCapacityAsync(groupId, ct);
            var (minSize, maxSize) = await queries.GetGroupSizePolicyAsync(detail.SemesterId, ct);
            if (req.MaxMembers.Value < minSize || req.MaxMembers.Value > maxSize)
                throw new InvalidOperationException($"Members must be between {minSize} and {maxSize} in this semester");
            if (req.MaxMembers.Value < activeCount)
                throw new InvalidOperationException($"MaxMembers cannot be less than current active members ({activeCount})");
        }

        await repo.UpdateGroupAsync(groupId, req.Name, req.Description, req.MaxMembers, req.MajorId, null, null, null, ct);
    }
    public async Task ForceRemoveMemberAsync(Guid groupId, Guid leaderUserId, Guid targetUserId, CancellationToken ct)
    {
        var isLeader = await queries.IsLeaderAsync(groupId, leaderUserId, ct);
        if (!isLeader) throw new UnauthorizedAccessException("Leader only");
        if (leaderUserId == targetUserId)
            throw new InvalidOperationException("Cannot remove yourself. Use leave or transfer leadership");

        var targetIsLeader = await queries.IsLeaderAsync(groupId, targetUserId, ct);
        if (targetIsLeader)
            throw new InvalidOperationException("Cannot remove leader. Transfer leadership first");

        var detail = await queries.GetGroupAsync(groupId, ct) ?? throw new KeyNotFoundException("Group not found");
        if (string.Equals(detail.Status, "active", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Cannot remove members while group is active");

        var ok = await repo.LeaveGroupAsync(groupId, targetUserId, ct);
        if (!ok) throw new KeyNotFoundException("Member not found");
        await LogAsync(new ActivityLogCreateRequest(leaderUserId, "group", "GROUP_MEMBER_REMOVED")
        {
            GroupId = groupId,
            EntityId = groupId,
            TargetUserId = targetUserId,
            Message = $"Leader removed user {targetUserId}"
        }, ct);

        await SendRemovalEmailAsync(groupId, targetUserId, leaderUserId, ct);
    }
    public async Task<IReadOnlyList<GroupMemberRoleDto>> ListMemberRolesAsync(Guid groupId, Guid currentUserId, Guid memberUserId, CancellationToken ct)
    {
        var isLeader = await queries.IsLeaderAsync(groupId, currentUserId, ct);
        if (!isLeader) throw new UnauthorizedAccessException("Leader only");
        return await repo.ListMemberRolesAsync(groupId, memberUserId, ct);
    }

    public async Task AddMemberRoleAsync(Guid groupId, Guid currentUserId, Guid memberUserId, string roleName, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(roleName))
            throw new ArgumentException("roleName is required");

        var isLeader = await queries.IsLeaderAsync(groupId, currentUserId, ct);
        if (!isLeader) throw new UnauthorizedAccessException("Leader only");

        await repo.AddMemberRoleAsync(groupId, memberUserId, currentUserId, roleName, ct);
    }

    public async Task RemoveMemberRoleAsync(Guid groupId, Guid currentUserId, Guid memberUserId, string roleName, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(roleName))
            throw new ArgumentException("roleName is required");

        var isLeader = await queries.IsLeaderAsync(groupId, currentUserId, ct);
        if (!isLeader) throw new UnauthorizedAccessException("Leader only");

        await repo.RemoveMemberRoleAsync(groupId, memberUserId, roleName, ct);
    }

    public async Task ReplaceMemberRolesAsync(Guid groupId, Guid currentUserId, Guid memberUserId, IReadOnlyCollection<string> roles, CancellationToken ct)
    {
        var isLeader = await queries.IsLeaderAsync(groupId, currentUserId, ct);
        if (!isLeader) throw new UnauthorizedAccessException("Leader only");

        await repo.ReplaceMemberRolesAsync(groupId, memberUserId, currentUserId, roles, ct);
    }

    private Task LogAsync(ActivityLogCreateRequest request, CancellationToken ct)
        => _activityLog.LogAsync(request, ct);

    private async Task SendRemovalEmailAsync(Guid groupId, Guid removedUserId, Guid actedByUserId, CancellationToken ct)
    {
        var removedUser = await _userQueries.GetAdminDetailAsync(removedUserId, ct);
        if (removedUser?.Email is null) return;
        var actedBy = await _userQueries.GetAdminDetailAsync(actedByUserId, ct);
        var group = await queries.GetGroupAsync(groupId, ct);
        var groupName = group?.Name ?? "your group";
        var subject = $"{AppName} - You were removed from {groupName}";
        var message = actedBy is null
            ? $"<p>You have been removed from the group <strong>{System.Net.WebUtility.HtmlEncode(groupName)}</strong>.</p>"
            : $"<p>{System.Net.WebUtility.HtmlEncode(actedBy.DisplayName ?? actedBy.Email ?? "Group leader")} removed you from the group <strong>{System.Net.WebUtility.HtmlEncode(groupName)}</strong>.</p>";
        var messageHtml = $@"{message}
<p style=""margin-top:8px;color:#475569;"">If you believe this is a mistake, please contact the leader.</p>";
        var html = EmailTemplateBuilder.Build(
            subject,
            "Removed from group",
            messageHtml,
            "Open Teammy",
            DefaultAppUrl);

        await _emailSender.SendAsync(
            removedUser.Email,
            subject,
            html,
            ct,
            replyToEmail: actedBy?.Email,
            fromDisplayName: actedBy?.DisplayName);
    }

    private async Task SendMemberLeftLeaderEmailAsync(Guid groupId, Guid memberUserId, CancellationToken ct)
    {
        var leaders = await queries.ListActiveMembersAsync(groupId, ct);
        var recipients = leaders
            .Where(m => string.Equals(m.Role, "leader", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(m.Email))
            .ToList();
        if (recipients.Count == 0) return;

        var member = await _userQueries.GetAdminDetailAsync(memberUserId, ct);
        var memberName = member?.DisplayName ?? member?.Email ?? "A member";
        var group = await queries.GetGroupAsync(groupId, ct);
        var groupName = group?.Name ?? "your group";
        var subject = $"{AppName} - {memberName} left {groupName}";
        var messageHtml = $@"<p>{System.Net.WebUtility.HtmlEncode(memberName)} has left <strong>{System.Net.WebUtility.HtmlEncode(groupName)}</strong>.</p>
<p style=""margin-top:8px;color:#475569;"">Please review your member list if needed.</p>";
        var html = EmailTemplateBuilder.Build(
            subject,
            "Member left group",
            messageHtml,
            "Open Teammy",
            DefaultAppUrl);

        foreach (var leader in recipients)
        {
            await _emailSender.SendAsync(
                leader.Email!,
                subject,
                html,
                ct,
                fromDisplayName: groupName);
        }
    }

    private async Task SendCloseRequestEmailToMentorAsync(Guid groupId, GroupMentorDto mentor, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(mentor.Email))
            return;

        var group = await queries.GetGroupAsync(groupId, ct);
        var groupName = group?.Name ?? "your group";
        var subject = $"{AppName} - Close request for {groupName}";
        var messageHtml = $@"<p>The leader has requested to close <strong>{System.Net.WebUtility.HtmlEncode(groupName)}</strong>.</p>
<p style=""margin-top:8px;color:#475569;"">Please review and confirm the close request.</p>";
        var html = EmailTemplateBuilder.Build(
            subject,
            "Confirm group close",
            messageHtml,
            "Review Close Request",
            DefaultAppUrl);

        await _emailSender.SendAsync(
            mentor.Email!,
            subject,
            html,
            ct,
            fromDisplayName: groupName);
    }

    private async Task SendCloseConfirmedEmailToLeadersAsync(Guid groupId, GroupMentorDto mentor, CancellationToken ct)
    {
        var leaders = await queries.ListActiveMembersAsync(groupId, ct);
        var recipients = leaders
            .Where(m => string.Equals(m.Role, "leader", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(m.Email))
            .ToList();
        if (recipients.Count == 0) return;

        var group = await queries.GetGroupAsync(groupId, ct);
        var groupName = group?.Name ?? "your group";
        var mentorName = mentor.DisplayName ?? mentor.Email ?? "Mentor";
        var subject = $"{AppName} - {groupName} is closed";
        var messageHtml = $@"<p>{System.Net.WebUtility.HtmlEncode(mentorName)} confirmed and closed <strong>{System.Net.WebUtility.HtmlEncode(groupName)}</strong>.</p>
<p style=""margin-top:8px;color:#475569;"">You can review the final status in Teammy.</p>";
        var html = EmailTemplateBuilder.Build(
            subject,
            "Group closed",
            messageHtml,
            "Open Teammy",
            DefaultAppUrl);

        foreach (var leader in recipients)
        {
            await _emailSender.SendAsync(
                leader.Email!,
                subject,
                html,
                ct,
                fromDisplayName: mentorName);
        }
    }

    private async Task SendCloseRejectedEmailToLeadersAsync(Guid groupId, GroupMentorDto mentor, CancellationToken ct)
    {
        var leaders = await queries.ListActiveMembersAsync(groupId, ct);
        var recipients = leaders
            .Where(m => string.Equals(m.Role, "leader", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(m.Email))
            .ToList();
        if (recipients.Count == 0) return;

        var group = await queries.GetGroupAsync(groupId, ct);
        var groupName = group?.Name ?? "your group";
        var mentorName = mentor.DisplayName ?? mentor.Email ?? "Mentor";
        var subject = $"{AppName} - Close request rejected for {groupName}";
        var messageHtml = $@"<p>{System.Net.WebUtility.HtmlEncode(mentorName)} rejected the close request for <strong>{System.Net.WebUtility.HtmlEncode(groupName)}</strong>.</p>
<p style=""margin-top:8px;color:#475569;"">The group remains in recruiting status.</p>";
        var html = EmailTemplateBuilder.Build(
            subject,
            "Close request rejected",
            messageHtml,
            "Open Teammy",
            DefaultAppUrl);

        foreach (var leader in recipients)
        {
            await _emailSender.SendAsync(
                leader.Email!,
                subject,
                html,
                ct,
                fromDisplayName: mentorName);
        }
    }

    private Task NotifyCloseStatusAsync(Guid groupId, Guid userId, string status, string action, CancellationToken ct)
        => _groupStatusNotifier.NotifyGroupStatusAsync(groupId, userId, status, action, ct);

    private async Task NotifyCloseStatusToLeadersAsync(Guid groupId, string status, string action, CancellationToken ct)
    {
        var members = await queries.ListActiveMembersAsync(groupId, ct);
        var leaderIds = members
            .Where(m => string.Equals(m.Role, "leader", StringComparison.OrdinalIgnoreCase))
            .Select(m => m.UserId)
            .Distinct()
            .ToList();
        foreach (var leaderId in leaderIds)
        {
            await _groupStatusNotifier.NotifyGroupStatusAsync(groupId, leaderId, status, action, ct);
        }
    }

    private sealed record ActivationSnapshot(
        Guid GroupId,
        Guid SemesterId,
        string Status,
        bool HasTopic,
        bool HasMentor,
        int CurrentMembers,
        int MaxMembers);

    private sealed record ActivationReadinessResult(bool Ready, string? ErrorMessage);

    private async Task<ActivationReadinessResult> EvaluateActivationReadinessAsync(GroupDetailDto detail, CancellationToken ct)
    {
        if (!detail.TopicId.HasValue)
            return new ActivationReadinessResult(false, "Group must select a topic and mentor before confirming");
        var mentor = await queries.GetMentorAsync(detail.Id, ct);
        if (mentor is null)
            return new ActivationReadinessResult(false, "Mentor has not confirmed this group");
        var (maxMembers, activeCount) = await queries.GetGroupCapacityAsync(detail.Id, ct);
        if (activeCount < maxMembers)
            return new ActivationReadinessResult(false, $"Need {maxMembers - activeCount} more member(s) to activate");
        return new ActivationReadinessResult(true, null);
    }

    private async Task<bool> AutoActivateEligibleGroupsAsync(IEnumerable<ActivationSnapshot> snapshots, CancellationToken ct)
    {
        var activatedAny = false;
        foreach (var s in snapshots)
        {
            if (await AutoActivateGroupAsync(s, ct))
                activatedAny = true;
        }
        return activatedAny;
    }

    private async Task<bool> AutoCloseExpiredGroupsAsync(IEnumerable<ActivationSnapshot> snapshots, CancellationToken ct)
    {
        var closedAny = false;
        foreach (var s in snapshots)
        {
            if (await MaybeAutoCloseGroupAsync(s, ct))
                closedAny = true;
        }
        return closedAny;
    }

    private async Task<bool> AutoActivateGroupAsync(ActivationSnapshot snapshot, CancellationToken ct)
    {
        if (!string.Equals(snapshot.Status, "recruiting", StringComparison.OrdinalIgnoreCase))
            return false;
        if (!snapshot.HasTopic || !snapshot.HasMentor)
            return false;
        if (snapshot.CurrentMembers < snapshot.MaxMembers)
            return false;

        var policy = await GetSemesterPolicyAsync(snapshot.SemesterId, ct);
        if (policy is null)
            return false;
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        if (today <= policy.TeamSelfSelectEnd)
            return false;

        await ActivateGroupInternalAsync(snapshot.GroupId, null, "deadline_auto_activation", ct);
        return true;
    }

    private async Task ActivateGroupInternalAsync(Guid groupId, Guid? actorUserId, string trigger, CancellationToken ct)
    {
        await repo.SetStatusAsync(groupId, "active", ct);
        await _postRepo.CloseAllOpenPostsForGroupAsync(groupId, ct);
        var action = trigger == "Leader confirmated!!!" ? "GROUP_CONFIRMED_ACTIVE" : "GROUP_AUTO_ACTIVATED";
        var message = trigger == "Leader confirmated!!!"
            ? "Leader confirmed the group is ready and activated it"
            : "Group automatically activated after team self-selection window ended";
        var resolvedActorId = actorUserId ?? await TryResolveDefaultActorAsync(groupId, ct);
        if (resolvedActorId.HasValue)
        {
            await LogAsync(new ActivityLogCreateRequest(resolvedActorId.Value, "group", action)
            {
                GroupId = groupId,
                EntityId = groupId,
                Message = message,
                Metadata = new { trigger }
            }, ct);
        }
    }

    private async Task<SemesterPolicyDto?> GetSemesterPolicyAsync(Guid semesterId, CancellationToken ct)
    {
        if (_semesterPolicyCache.TryGetValue(semesterId, out var cached))
            return cached;
        var policy = await _semesterQueries.GetPolicyAsync(semesterId, ct);
        _semesterPolicyCache[semesterId] = policy;
        return policy;
    }

    private async Task<SemesterDetailDto?> GetSemesterDetailAsync(Guid semesterId, CancellationToken ct)
    {
        if (_semesterDetailCache.TryGetValue(semesterId, out var cached))
            return cached;
        var detail = await _semesterQueries.GetByIdAsync(semesterId, ct);
        _semesterDetailCache[semesterId] = detail;
        return detail;
    }

    private async Task<bool> MaybeAutoCloseGroupAsync(ActivationSnapshot snapshot, CancellationToken ct)
    {
        if (string.Equals(snapshot.Status, "closed", StringComparison.OrdinalIgnoreCase))
            return false;
        var semester = await GetSemesterDetailAsync(snapshot.SemesterId, ct);
        if (semester is null)
            return false;
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        if (today <= semester.EndDate)
            return false;

        await repo.CloseGroupAsync(snapshot.GroupId, ct);
        var actorId = await TryResolveDefaultActorAsync(snapshot.GroupId, ct);
        if (actorId.HasValue)
        {
            await LogAsync(new ActivityLogCreateRequest(actorId.Value, "group", "GROUP_AUTO_CLOSED")
            {
                GroupId = snapshot.GroupId,
                EntityId = snapshot.GroupId,
                Message = "Group automatically closed after semester ended",
                Metadata = new { reason = "semester_end" }
            }, ct);
        }
        return true;
    }

    private async Task<Guid?> TryResolveDefaultActorAsync(Guid groupId, CancellationToken ct)
    {
        var members = await queries.ListActiveMembersAsync(groupId, ct);
        var leader = members.FirstOrDefault(m => string.Equals(m.Role, "leader", StringComparison.OrdinalIgnoreCase));
        if (leader is not null)
            return leader.UserId;
        if (members.Count > 0)
            return members[0].UserId;
        var mentor = await queries.GetMentorAsync(groupId, ct);
        return mentor?.UserId;
    }
}
