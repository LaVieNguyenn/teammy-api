using Teammy.Application.Common.Interfaces;
using Teammy.Application.Groups.Dtos;

namespace Teammy.Application.Groups.Services;

public sealed class GroupService(
    IGroupRepository repo,
    IGroupReadOnlyQueries queries,
    IUserReadOnlyQueries userQueries,
    IRecruitmentPostRepository postRepo)
{
    private readonly IUserReadOnlyQueries _userQueries = userQueries;
    private readonly IRecruitmentPostRepository _postRepo = postRepo;

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

        var groupId = await repo.CreateGroupAsync(semesterId, null, majorId, req.Name, req.Description, req.MaxMembers, ct);
        await repo.AddMembershipAsync(groupId, creatorUserId, semesterId, "leader", ct);
        await _postRepo.DeleteProfilePostsForUserAsync(creatorUserId, semesterId, ct);
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
            var (minSize, maxSize) = await queries.GetGroupSizePolicyAsync(detail.SemesterId, ct);
            if (req.MaxMembers.Value < minSize || req.MaxMembers.Value > maxSize)
                throw new InvalidOperationException($"Members must be between {minSize} and {maxSize} in this semester");
            if (req.MaxMembers.Value < activeCount)
                throw new InvalidOperationException($"MaxMembers cannot be less than current active members ({activeCount})");
        }

        await repo.UpdateGroupAsync(groupId, req.Name, req.Description, req.MaxMembers, req.MajorId, null, null, ct);
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
    }
}
