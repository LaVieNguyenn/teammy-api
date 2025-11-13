using Teammy.Application.Common.Interfaces;
using Teammy.Application.Groups.Dtos;

namespace Teammy.Application.Groups.Services;

public sealed class GroupService(
    IGroupRepository repo,
    IGroupReadOnlyQueries queries,
    IRecruitmentPostRepository postRepo,
    IRecruitmentPostReadOnlyQueries postReadQueries)
{
    public async Task<Guid> CreateGroupAsync(Guid creatorUserId, CreateGroupRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Name))
            throw new ArgumentException("Name is required");
        if (req.MaxMembers <= 0)
            throw new ArgumentException("MaxMembers must be > 0");
        if (req.MaxMembers < 4 || req.MaxMembers > 6)
            throw new ArgumentException("MaxMembers must be between 4 and 6");

        var semesterId = req.SemesterId ?? await queries.GetActiveSemesterIdAsync(ct)
            ?? throw new InvalidOperationException("No active semester and no semesterId provided");

        var hasActive = await queries.HasActiveMembershipInSemesterAsync(creatorUserId, semesterId, ct);
        if (hasActive)
            throw new InvalidOperationException("User already has active/pending membership in this semester");

        var groupId = await repo.CreateGroupAsync(semesterId, req.TopicId, req.MajorId, req.Name, req.Description, req.MaxMembers, ct);
        await repo.AddMembershipAsync(groupId, creatorUserId, semesterId, "leader", ct);
        return groupId;
    }

    public Task<IReadOnlyList<GroupSummaryDto>> ListGroupsAsync(string? status, Guid? majorId, Guid? topicId, CancellationToken ct)
        => queries.ListGroupsAsync(status, majorId, topicId, ct);

    public Task<GroupDetailDto?> GetGroupAsync(Guid id, CancellationToken ct)
        => queries.GetGroupAsync(id, ct);

    public async Task ApplyToGroupAsync(Guid groupId, Guid userId, CancellationToken ct)
    {
        var (maxMembers, activeCount) = await queries.GetGroupCapacityAsync(groupId, ct);
        if (activeCount >= maxMembers)
            throw new InvalidOperationException("Group is full");

        var detail = await queries.GetGroupAsync(groupId, ct) ?? throw new KeyNotFoundException("Group not found");

        var hasActive = await queries.HasActiveMembershipInSemesterAsync(userId, detail.SemesterId, ct);
        if (hasActive)
            throw new InvalidOperationException("User already has active/pending membership in this semester");

        // Guard: if user already has a pending application to this group via any post -> conflict
        var pendingApp = await postReadQueries.FindPendingApplicationInGroupAsync(groupId, userId, ct);
        if (pendingApp.HasValue)
        {
            var (appId, postId) = pendingApp.Value;
            throw new InvalidOperationException($"already_applied:{appId}:{postId}");
        }

        await repo.AddMembershipAsync(groupId, userId, detail.SemesterId, "pending", ct);
    }

    public async Task LeaveGroupAsync(Guid groupId, Guid userId, CancellationToken ct)
    {
        var ok = await repo.LeaveGroupAsync(groupId, userId, ct);
        if (!ok)
            throw new InvalidOperationException("Not a member of this group");
    }

    public async Task<IReadOnlyList<JoinRequestDto>> ListJoinRequestsAsync(Guid groupId, Guid currentUserId, CancellationToken ct)
    {
        var isLeader = await queries.IsLeaderAsync(groupId, currentUserId, ct);
        if (!isLeader) throw new UnauthorizedAccessException("Leader only");
        return await queries.GetPendingJoinRequestsAsync(groupId, ct);
    }

    public async Task AcceptJoinRequestAsync(Guid groupId, Guid reqId, Guid currentUserId, CancellationToken ct)
    {
        var isLeader = await queries.IsLeaderAsync(groupId, currentUserId, ct);
        if (!isLeader) throw new UnauthorizedAccessException("Leader only");

        var (maxMembers, activeCount) = await queries.GetGroupCapacityAsync(groupId, ct);
        if (activeCount >= maxMembers)
            throw new InvalidOperationException("Group is full");

        // Find requester userId from pending list
        var pendings = await queries.GetPendingJoinRequestsAsync(groupId, ct);
        var req = pendings.FirstOrDefault(x => x.RequestId == reqId) ?? throw new KeyNotFoundException("Join request not found");

        await repo.UpdateMembershipStatusAsync(reqId, "member", ct);

        // Cleanup duplicate applications for this user to the same group
        await postRepo.RejectPendingApplicationsForUserInGroupAsync(groupId, req.UserId, ct);

        // If group now full, set open posts to full
        var (_, newActiveCount) = await queries.GetGroupCapacityAsync(groupId, ct);
        if (newActiveCount >= maxMembers)
        {
            await postRepo.SetOpenPostsStatusForGroupAsync(groupId, "full", ct);
        }
    }

    public async Task RejectJoinRequestAsync(Guid groupId, Guid reqId, Guid currentUserId, CancellationToken ct)
    {
        var isLeader = await queries.IsLeaderAsync(groupId, currentUserId, ct);
        if (!isLeader) throw new UnauthorizedAccessException("Leader only");
        await repo.DeleteMembershipAsync(reqId, ct);
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
            if (req.MaxMembers.Value < 4 || req.MaxMembers.Value > 5)
                throw new InvalidOperationException("MaxMembers must be between 4 and 5");
            if (req.MaxMembers.Value < activeCount)
                throw new InvalidOperationException($"MaxMembers cannot be less than current active members ({activeCount})");
        }

        // Only allow selecting a topic when the group is full
        var setTopicAndActivate = false;
        if (req.TopicId.HasValue)
        {
            var (maxMembers, activeCount) = await queries.GetGroupCapacityAsync(groupId, ct);
            if (activeCount < maxMembers)
                throw new InvalidOperationException("Group must be full to select a topic");
            setTopicAndActivate = true;
        }

        await repo.UpdateGroupAsync(groupId, req.Name, req.Description, req.MaxMembers, req.MajorId, req.TopicId, ct);

        if (setTopicAndActivate)
        {
            // Set group to active and mark open posts as full
            await repo.SetStatusAsync(groupId, "active", ct);
            await postRepo.SetOpenPostsStatusForGroupAsync(groupId, "full", ct);
        }
    }

    // Leader kicks a member or cancels a pending join-request
    public async Task ForceRemoveMemberAsync(Guid groupId, Guid leaderUserId, Guid targetUserId, CancellationToken ct)
    {
        var isLeader = await queries.IsLeaderAsync(groupId, leaderUserId, ct);
        if (!isLeader) throw new UnauthorizedAccessException("Leader only");
        if (leaderUserId == targetUserId)
            throw new InvalidOperationException("Cannot remove yourself. Use leave or transfer leadership");

        var targetIsLeader = await queries.IsLeaderAsync(groupId, targetUserId, ct);
        if (targetIsLeader)
            throw new InvalidOperationException("Cannot remove leader. Transfer leadership first");

        var ok = await repo.LeaveGroupAsync(groupId, targetUserId, ct);
        if (!ok) throw new KeyNotFoundException("Member not found");
    }
}
