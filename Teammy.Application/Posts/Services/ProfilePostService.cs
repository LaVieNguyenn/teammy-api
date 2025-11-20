using Teammy.Application.Common.Interfaces;
using Teammy.Application.Posts.Dtos;

namespace Teammy.Application.Posts.Services;

public sealed class ProfilePostService(
    IRecruitmentPostRepository repo,
    IRecruitmentPostReadOnlyQueries queries,
    IGroupReadOnlyQueries groupQueries,
    IGroupRepository groupRepo)
{
    public async Task<Guid> CreateAsync(Guid currentUserId, CreateProfilePostRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Title)) throw new ArgumentException("Title is required");
        var semesterId = await queries.GetActiveSemesterIdAsync(ct) ?? throw new InvalidOperationException("No active semester");
        var userInActiveGroup = await groupQueries.HasActiveGroupAsync(currentUserId, semesterId, ct);
        if (userInActiveGroup)
            throw new InvalidOperationException("Members of active groups cannot create profile posts");
        // Reuse recruitment_post with post_type = 'profile' and user_id set
        // GroupId here is null
        return await repo.CreateRecruitmentPostAsync(semesterId, postType: "individual", groupId: null, userId: currentUserId, req.MajorId, req.Title, req.Description, req.Skills, null, ct);
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

        var (maxMembers, activeCount) = await groupQueries.GetGroupCapacityAsync(groupId, ct);
        if (activeCount >= maxMembers)
            throw new InvalidOperationException("Group is full");

        var hasActive = await groupQueries.HasActiveMembershipInSemesterAsync(
            owner.OwnerUserId.Value,
            owner.SemesterId,
            ct);

        if (hasActive)
            throw new InvalidOperationException("User already has active/pending membership in this semester");

        var existing = await queries.FindApplicationByPostAndGroupAsync(profilePostId, groupId, ct);
        if (existing.HasValue)
        {
            var (applicationId, status) = existing.Value;

            if (string.Equals(status, "pending", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"already_invited:{applicationId}");

            if (string.Equals(status, "accepted", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"already_accepted:{applicationId}");

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
}
