using Teammy.Application.Common.Interfaces;
using Teammy.Application.Posts.Dtos;

namespace Teammy.Application.Posts.Services;

public sealed class RecruitmentPostService(
    IRecruitmentPostRepository repo,
    IRecruitmentPostReadOnlyQueries queries,
    IGroupReadOnlyQueries groupQueries,
    IGroupRepository groupRepo)
{
    public async Task<Guid> CreateAsync(Guid currentUserId, CreateRecruitmentPostRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Title)) throw new ArgumentException("Title is required");
        var (maxMembers, activeCount) = await groupQueries.GetGroupCapacityAsync(req.GroupId, ct);
        if (activeCount >= maxMembers) throw new InvalidOperationException("Group is full");

        var detail = await groupQueries.GetGroupAsync(req.GroupId, ct) ?? throw new KeyNotFoundException("Group not found");
        var isLeader = await groupQueries.IsLeaderAsync(req.GroupId, currentUserId, ct);
        if (!isLeader) throw new UnauthorizedAccessException("Leader only");

        var semesterId = detail.SemesterId;
        var postId = await repo.CreateRecruitmentPostAsync(semesterId, postType: "group", groupId: req.GroupId, userId: null, req.MajorId, req.Title, req.Description, req.Skills, ct);
        return postId;
    }

    public async Task<IReadOnlyList<RecruitmentPostSummaryDto>> ListAsync(string? skills, Guid? majorId, string? status, CancellationToken ct)
    {
        var items = await queries.ListAsync(skills, majorId, status, ct);
        return items.Where(x => x.GroupId != null).ToList();
    }

    public async Task<RecruitmentPostDetailDto?> GetAsync(Guid id, CancellationToken ct)
    {
        var d = await queries.GetAsync(id, ct);
        if (d is null || d.GroupId is null) return null;
        return d;
    }

    public async Task ApplyAsync(Guid postId, Guid userId, string? message, CancellationToken ct)
    {
        var owner = await queries.GetPostOwnerAsync(postId, ct);
        if (owner.GroupId is null) throw new InvalidOperationException("Post does not accept applications");

        var (maxMembers, activeCount) = await groupQueries.GetGroupCapacityAsync(owner.GroupId.Value, ct);
        if (activeCount >= maxMembers) throw new InvalidOperationException("Group is full");

        var hasActive = await groupQueries.HasActiveMembershipInSemesterAsync(userId, owner.SemesterId, ct);
        if (hasActive) throw new InvalidOperationException("User already has active/pending membership in this semester");

        await repo.CreateApplicationAsync(postId, userId, null, userId, message, ct);
    }

    public Task<IReadOnlyList<ApplicationDto>> ListApplicationsAsync(Guid postId, Guid currentUserId, CancellationToken ct)
        => EnsureOwner(postId, currentUserId, ct, () => queries.ListApplicationsAsync(postId, ct));

    public Task UpdateAsync(Guid postId, Guid currentUserId, UpdateRecruitmentPostRequest req, CancellationToken ct)
        => EnsureOwner(postId, currentUserId, ct, () => repo.UpdatePostAsync(postId, req.Title, req.Description, req.Skills, req.Status, ct));

    public async Task AcceptAsync(Guid postId, Guid appId, Guid currentUserId, CancellationToken ct)
    {
        var owner = await EnsureOwnerAndGet(postId, currentUserId, ct);
        var (maxMembers, activeCount) = await groupQueries.GetGroupCapacityAsync(owner.GroupId!.Value, ct);
        if (activeCount >= maxMembers) throw new InvalidOperationException("Group is full");
        // Load application
        var apps = await queries.ListApplicationsAsync(postId, ct);
        var app = apps.FirstOrDefault(a => a.ApplicationId == appId) ?? throw new KeyNotFoundException("Application not found");
        if (app.ApplicantUserId is null) throw new InvalidOperationException("Invalid application");

        var hasActive = await groupQueries.HasActiveMembershipInSemesterAsync(app.ApplicantUserId.Value, owner.SemesterId, ct);
        if (hasActive) throw new InvalidOperationException("User already has active/pending membership in this semester");

        await groupRepo.AddMembershipAsync(owner.GroupId.Value, app.ApplicantUserId.Value, owner.SemesterId, "member", ct);
        await repo.UpdateApplicationStatusAsync(appId, "accepted", ct);
    }

    public async Task RejectAsync(Guid postId, Guid appId, Guid currentUserId, CancellationToken ct)
    {
        await EnsureOwner(postId, currentUserId, ct, () => repo.UpdateApplicationStatusAsync(appId, "rejected", ct));
    }

    private async Task<T> EnsureOwner<T>(Guid postId, Guid currentUserId, CancellationToken ct, Func<Task<T>> action)
    {
        var owner = await queries.GetPostOwnerAsync(postId, ct);
        if (owner.GroupId is null) throw new UnauthorizedAccessException("Not a group post");
        var isLeader = await groupQueries.IsLeaderAsync(owner.GroupId.Value, currentUserId, ct);
        if (!isLeader) throw new UnauthorizedAccessException("Leader only");
        return await action();
    }

    private async Task EnsureOwner(Guid postId, Guid currentUserId, CancellationToken ct, Func<Task> action)
    {
        var owner = await queries.GetPostOwnerAsync(postId, ct);
        if (owner.GroupId is null) throw new UnauthorizedAccessException("Not a group post");
        var isLeader = await groupQueries.IsLeaderAsync(owner.GroupId.Value, currentUserId, ct);
        if (!isLeader) throw new UnauthorizedAccessException("Leader only");
        await action();
    }

    private async Task<(Guid? GroupId, Guid SemesterId, Guid? OwnerUserId)> EnsureOwnerAndGet(Guid postId, Guid currentUserId, CancellationToken ct)
    {
        var owner = await queries.GetPostOwnerAsync(postId, ct);
        if (owner.GroupId is null) throw new UnauthorizedAccessException("Not a group post");
        var isLeader = await groupQueries.IsLeaderAsync(owner.GroupId.Value, currentUserId, ct);
        if (!isLeader) throw new UnauthorizedAccessException("Leader only");
        return owner;
    }
}
