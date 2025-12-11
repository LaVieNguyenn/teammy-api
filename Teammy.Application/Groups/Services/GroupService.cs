using System.Linq;
using System.Net;
using System.Text.Json;
using Teammy.Application.Activity.Dtos;
using Teammy.Application.Activity.Services;
using Teammy.Application.Common.Interfaces;
using Teammy.Application.Groups.Dtos;

namespace Teammy.Application.Groups.Services;

public sealed class GroupService(
    IGroupRepository repo,
    IGroupReadOnlyQueries queries,
    IUserReadOnlyQueries userQueries,
    IRecruitmentPostRepository postRepo,
    ActivityLogService activityLogService,
    IEmailSender emailSender)
{
    private const string AppName = "TEAMMY";
    private readonly IUserReadOnlyQueries _userQueries = userQueries;
    private readonly IRecruitmentPostRepository _postRepo = postRepo;
    private readonly ActivityLogService _activityLog = activityLogService;
    private readonly IEmailSender _emailSender = emailSender;

    public async Task<Guid> CreateGroupAsync(Guid creatorUserId, CreateGroupRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Name))
            throw new ArgumentException("Name is required");
        var semesterId = req.SemesterId ?? await queries.GetActiveSemesterIdAsync(ct)
            ?? throw new InvalidOperationException("No active semester and no semesterId provided");

        var (minSize, maxSize) = await queries.GetGroupSizePolicyAsync(semesterId, ct);
        if (req.MaxMembers < minSize || req.MaxMembers > maxSize)
            throw new ArgumentException($"Members must be between {minSize} and {maxSize} in this semester");

        var hasActive = await queries.HasActiveMembershipInSemesterAsync(creatorUserId, semesterId, ct);
        if (hasActive)
            throw new InvalidOperationException("User already has active/pending membership in this semester");

        var creator = await _userQueries.GetAdminDetailAsync(creatorUserId, ct)
            ?? throw new InvalidOperationException("User not found");
        var majorId = creator.MajorId ?? req.MajorId;
        if (!majorId.HasValue)
            throw new InvalidOperationException("User hasn't major");

        if (req.TopicId.HasValue)
            throw new InvalidOperationException("Topic will be assigned after mentor confirmation");

        var skillsJson = req.Skills is null ? null : SerializeSkills(req.Skills);
        var groupId = await repo.CreateGroupAsync(semesterId, null, majorId, req.Name, req.Description, req.MaxMembers, skillsJson, ct);
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

    public Task<IReadOnlyList<GroupSummaryDto>> ListGroupsAsync(string? status, Guid? majorId, Guid? topicId, CancellationToken ct)
        => queries.ListGroupsAsync(status, majorId, topicId, ct);

    public Task<GroupDetailDto?> GetGroupAsync(Guid id, CancellationToken ct)
        => queries.GetGroupAsync(id, ct);

    public async Task LeaveGroupAsync(Guid groupId, Guid userId, CancellationToken ct)
    {
        var detail = await queries.GetGroupAsync(groupId, ct) ?? throw new KeyNotFoundException("Group not found");
        if (string.Equals(detail.Status, "active", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Cannot leave an active group");

        var isLeader = await queries.IsLeaderAsync(groupId, userId, ct);
        if (isLeader)
        {
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

    public Task<IReadOnlyList<MyGroupDto>> ListMyGroupsAsync(Guid currentUserId, Guid? semesterId, CancellationToken ct)
        => queries.ListMyGroupsAsync(currentUserId, semesterId, ct);

    public Task<IReadOnlyList<GroupMemberDto>> ListActiveMembersAsync(Guid groupId, CancellationToken ct)
        => queries.ListActiveMembersAsync(groupId, ct);

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

        string? skillsJson = req.Skills is null ? null : SerializeSkills(req.Skills);
        await repo.UpdateGroupAsync(groupId, req.Name, req.Description, req.MaxMembers, req.MajorId, null, null, skillsJson, ct);
    }

    // Leader remove a member 
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

    private static string SerializeSkills(IReadOnlyCollection<string> skills)
    {
        var normalized = skills
            .Select(s => s?.Trim())
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        return JsonSerializer.Serialize(normalized);
    }

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
        var html = $@"<!doctype html>
<html><body style=""font-family:Segoe UI,Arial,Helvetica,sans-serif;color:#0f172a"">
{message}
<p>If you believe this is a mistake, please contact the leader.</p>
</body></html>";

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
        var html = $@"<!doctype html>
<html><body style=""font-family:Segoe UI,Arial,Helvetica,sans-serif;color:#0f172a"">
<p>{System.Net.WebUtility.HtmlEncode(memberName)} has left <strong>{System.Net.WebUtility.HtmlEncode(groupName)}</strong>.</p>
<p>Please review your member list if needed.</p>
</body></html>";

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
}
