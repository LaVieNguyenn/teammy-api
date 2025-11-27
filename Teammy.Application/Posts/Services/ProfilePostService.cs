using Teammy.Application.Common.Interfaces;
using Teammy.Application.Posts.Dtos;

namespace Teammy.Application.Posts.Services;

public sealed class ProfilePostService(
    IRecruitmentPostRepository repo,
    IRecruitmentPostReadOnlyQueries queries,
    IGroupReadOnlyQueries groupQueries,
    IGroupRepository groupRepo,
    IUserReadOnlyQueries userQueries)
{
    private readonly IUserReadOnlyQueries _userQueries = userQueries;

    public async Task<Guid> CreateAsync(Guid currentUserId, CreateProfilePostRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Title)) throw new ArgumentException("Title is required");
        var semesterId = await queries.GetActiveSemesterIdAsync(ct) ?? throw new InvalidOperationException("No active semester");
        var userInActiveGroup = await groupQueries.HasActiveGroupAsync(currentUserId, semesterId, ct);
        if (userInActiveGroup)
            throw new InvalidOperationException("Members of active groups cannot create profile posts");
        var userDetail = await _userQueries.GetAdminDetailAsync(currentUserId, ct)
            ?? throw new InvalidOperationException("User not found");
        var targetMajor = req.MajorId ?? userDetail.MajorId;

        return await repo.CreateRecruitmentPostAsync(semesterId, postType: "individual", groupId: null, userId: currentUserId, targetMajor, req.Title, req.Description, req.Skills, null, ct);
    }

    public Task<ProfilePostDetailDto?> GetAsync(Guid id, ExpandOptions expand, CancellationToken ct)
        => queries.GetProfilePostAsync(id, expand, ct);

    public Task<IReadOnlyList<ProfilePostSummaryDto>> ListAsync(string? skills, Guid? majorId, string? status, ExpandOptions expand, CancellationToken ct)
        => queries.ListProfilePostsAsync(skills, majorId, status, expand, ct);

   public async Task InviteAsync(Guid profilePostId, Guid currentUserId, CancellationToken ct)
    {
        var owner = await queries.GetPostOwnerAsync(profilePostId, ct);
        if (owner.OwnerUserId is null)
            throw new InvalidOperationException("Not a profile post");

        if (!string.Equals(owner.Status, "open", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Profile post is not open");

        if (owner.ApplicationDeadline.HasValue && owner.ApplicationDeadline.Value <= DateTime.UtcNow)
        {
            await repo.UpdatePostAsync(profilePostId, null, null, null, "expired", ct);
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
        if (existing.HasValue)
        {
            var (applicationId, status) = existing.Value;

            if (string.Equals(status, "pending", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"already_invited:{applicationId}");

            await repo.ReactivateApplicationAsync(applicationId, null, ct);
            return;
        }
        await repo.CreateApplicationAsync(
            profilePostId,
            applicantUserId: null,
            applicantGroupId: groupId,
            appliedByUserId: currentUserId,
            message: null,
            ct);
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
    }
}
