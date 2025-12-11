using Teammy.Application.Common.Interfaces;
using System.Text.Json;
using Teammy.Application.Posts.Dtos;
using Teammy.Application.Posts.Templates;

namespace Teammy.Application.Posts.Services;

public sealed class RecruitmentPostService(
    IRecruitmentPostRepository repo,
    IRecruitmentPostReadOnlyQueries queries,
    IGroupReadOnlyQueries groupQueries,
    IGroupRepository groupRepo,
    IEmailSender emailSender,
    IUserReadOnlyQueries userQueries,
    IAppUrlProvider urlProvider)
{
    private const string AppName = "TEAMMY";
    private readonly IEmailSender _emailSender = emailSender;
    private readonly IUserReadOnlyQueries _userQueries = userQueries;
    private readonly IAppUrlProvider _urlProvider = urlProvider;

    public async Task<Guid> CreateAsync(Guid currentUserId, CreateRecruitmentPostRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Title)) throw new ArgumentException("Title is required");
        var (maxMembers, activeCount) = await groupQueries.GetGroupCapacityAsync(req.GroupId, ct);
        if (activeCount >= maxMembers) throw new InvalidOperationException("Group is full");

        var detail = await groupQueries.GetGroupAsync(req.GroupId, ct) ?? throw new KeyNotFoundException("Group not found");
        var isLeader = await groupQueries.IsLeaderAsync(req.GroupId, currentUserId, ct);
        if (!isLeader) throw new UnauthorizedAccessException("Leader only");
        if (string.Equals(detail.Status, "active", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Active group cannot create recruitment posts");

        var semesterId = detail.SemesterId;
        var targetMajorId = req.MajorId ?? detail.MajorId;
        await repo.CloseAllOpenPostsForGroupAsync(req.GroupId, ct);
        DateTime expiresAt;
       expiresAt = DateTime.SpecifyKind(req.ExpiresAt.Value, DateTimeKind.Local)
                     .ToUniversalTime();
        if (expiresAt <= DateTime.UtcNow)
            throw new ArgumentException("Expiration time must be in the future");
        var requiredSkillsJson = SerializeSkills(req.Skills);
        var postId = await repo.CreateRecruitmentPostAsync(
            semesterId,
            postType: "group_hiring",
            groupId: req.GroupId,
            userId: null,
            targetMajorId,
            req.Title,
            req.Description,
            req.PositionNeeded,
            requiredSkillsJson,
            expiresAt,
            ct);
        return postId;
    }

    public async Task<IReadOnlyList<RecruitmentPostSummaryDto>> ListAsync(string? skills, Guid? majorId, string? status, ExpandOptions expand, Guid? currentUserId, CancellationToken ct)
    {
        await repo.ExpireOpenPostsAsync(DateTime.UtcNow, ct);
        var items = await queries.ListAsync(skills, majorId, status, expand, currentUserId, ct);
        return items.Where(x => x.GroupId != null).ToList();
    }

    public async Task<RecruitmentPostDetailDto?> GetAsync(Guid id, ExpandOptions expand, Guid? currentUserId, CancellationToken ct)
    {
        await repo.ExpireOpenPostsAsync(DateTime.UtcNow, ct);
        var d = await queries.GetAsync(id, expand, currentUserId, ct);
        if (d is null || d.GroupId is null) return null;
        return d;
    }

    public async Task<IReadOnlyList<RecruitmentPostSummaryDto>> ListAppliedByUserAsync(Guid currentUserId, ExpandOptions expand, CancellationToken ct)
    {
        await repo.ExpireOpenPostsAsync(DateTime.UtcNow, ct);
        var items = await queries.ListAppliedByUserAsync(currentUserId, expand, ct);
        return items.Where(x => x.GroupId != null).ToList();
    }

    public async Task ApplyAsync(Guid postId, Guid userId, string? message, CancellationToken ct)
    {
        var owner = await queries.GetPostOwnerAsync(postId, ct);
        EnsureGroupPost(owner, throwUnauthorized: false);
        await EnsurePostIsOpenAsync(owner, postId, ct);
        var (maxMembers, activeCount) = await groupQueries.GetGroupCapacityAsync(owner.GroupId!.Value, ct);
        if (activeCount >= maxMembers) throw new InvalidOperationException("Group is full");
        var hasActive = await groupQueries.HasActiveMembershipInSemesterAsync(userId, owner.SemesterId, ct);
        if (hasActive) throw new InvalidOperationException("User already has active/pending membership in this semester");
        var existingApp = await queries.FindApplicationByPostAndUserAsync(postId, userId, ct);
        if (existingApp.HasValue)
        {
            var (appId, status) = existingApp.Value;
            if (string.Equals(status, "pending", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Already applied!!!");
            if (string.Equals(status, "accepted", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Already accepted!!!");
            await repo.ReactivateApplicationAsync(appId, message, ct);
            await NotifyLeadersAboutApplicationAsync(postId, owner.GroupId.Value, userId, message, ct);
            return;
        }
        await repo.CreateApplicationAsync(postId, userId, null, userId, message, ct);
        await NotifyLeadersAboutApplicationAsync(postId, owner.GroupId.Value, userId, message, ct);
    }

    public Task<IReadOnlyList<ApplicationDto>> ListApplicationsAsync(Guid postId, Guid currentUserId, CancellationToken ct)
        => EnsureOwner(postId, currentUserId, ct, () => queries.ListApplicationsAsync(postId, ct));

    public Task UpdateAsync(Guid postId, Guid currentUserId, UpdateRecruitmentPostRequest req, CancellationToken ct)
        => EnsureOwner(postId, currentUserId, ct, () =>
        {
            var requiredSkillsJson = SerializeSkills(req.Skills);
            return repo.UpdatePostAsync(postId, req.Title, req.Description, req.PositionNeeded, req.Status, requiredSkillsJson, ct);
        });

    public Task DeleteAsync(Guid postId, Guid currentUserId, CancellationToken ct)
        => EnsureOwner(postId, currentUserId, ct, () => repo.DeletePostAsync(postId, ct));

    public async Task AcceptAsync(Guid postId, Guid appId, Guid currentUserId, CancellationToken ct)
    {
        var owner = await EnsureOwnerAndGet(postId, currentUserId, ct);
        var (maxMembers, activeCount) = await groupQueries.GetGroupCapacityAsync(owner.GroupId!.Value, ct);
        if (activeCount >= maxMembers) throw new InvalidOperationException("Group is full");
        var app = await GetApplicationForPostAsync(postId, appId, ct);
        if (app.ApplicantUserId is null) throw new InvalidOperationException("Invalid application");
        var hasActive = await groupQueries.HasActiveMembershipInSemesterAsync(app.ApplicantUserId.Value, owner.SemesterId, ct);
        if (hasActive) throw new InvalidOperationException("User already has active/pending membership in this semester");
        await groupRepo.AddMembershipAsync(owner.GroupId.Value, app.ApplicantUserId.Value, owner.SemesterId, "member", ct);
        await repo.DeleteProfilePostsForUserAsync(app.ApplicantUserId.Value, owner.SemesterId, ct);
        await repo.UpdateApplicationStatusAsync(appId, "accepted", ct);
        await repo.RejectPendingApplicationsForUserInGroupAsync(owner.GroupId.Value, app.ApplicantUserId.Value, ct);
        await NotifyApplicantDecisionAsync(
            app.ApplicantUserId.Value,
            app.ApplicantEmail,
            app.ApplicantDisplayName,
            owner.GroupId.Value,
            postId,
            "accepted",
            ct);
    }

    public async Task RejectAsync(Guid postId, Guid appId, Guid currentUserId, CancellationToken ct)
    {
        var owner = await EnsureOwnerAndGet(postId, currentUserId, ct);
        var app = await GetApplicationForPostAsync(postId, appId, ct);
        if (app.ApplicantUserId is null) throw new InvalidOperationException("Invalid application");

        await repo.UpdateApplicationStatusAsync(appId, "rejected", ct);
        await NotifyApplicantDecisionAsync(
            app.ApplicantUserId.Value,
            app.ApplicantEmail,
            app.ApplicantDisplayName,
            owner.GroupId!.Value,
            postId,
            "rejected",
            ct);
    }

    public async Task WithdrawAsync(Guid postId, Guid appId, Guid currentUserId, CancellationToken ct)
    {
        var owner = await queries.GetPostOwnerAsync(postId, ct);
        EnsureGroupPost(owner, throwUnauthorized: false);

        var existingApp = await queries.FindApplicationByPostAndUserAsync(postId, currentUserId, ct);
        if (!existingApp.HasValue || existingApp.Value.ApplicationId != appId)
            throw new UnauthorizedAccessException("Not your application");

        var status = existingApp.Value.Status;
        if (!string.Equals(status, "pending", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Application already handled");

        await repo.UpdateApplicationStatusAsync(appId, "withdrawn", ct);
    }

    private async Task<T> EnsureOwner<T>(Guid postId, Guid currentUserId, CancellationToken ct, Func<Task<T>> action)
    {
        var owner = await queries.GetPostOwnerAsync(postId, ct);
        EnsureGroupPost(owner);
        var isLeader = await groupQueries.IsLeaderAsync(owner.GroupId!.Value, currentUserId, ct);
        if (!isLeader) throw new UnauthorizedAccessException("Leader only");
        return await action();
    }

    private async Task EnsureOwner(Guid postId, Guid currentUserId, CancellationToken ct, Func<Task> action)
    {
        var owner = await queries.GetPostOwnerAsync(postId, ct);
        EnsureGroupPost(owner);
        var isLeader = await groupQueries.IsLeaderAsync(owner.GroupId!.Value, currentUserId, ct);
        if (!isLeader) throw new UnauthorizedAccessException("Leader only");
        await action();
    }

    private async Task<(Guid? GroupId, Guid SemesterId, Guid? OwnerUserId, DateTime? ApplicationDeadline, string Status)> EnsureOwnerAndGet(Guid postId, Guid currentUserId, CancellationToken ct)
    {
        var owner = await queries.GetPostOwnerAsync(postId, ct);
        EnsureGroupPost(owner);
        var isLeader = await groupQueries.IsLeaderAsync(owner.GroupId!.Value, currentUserId, ct);
        if (!isLeader) throw new UnauthorizedAccessException("Leader only");
        return owner;
    }

    private async Task EnsurePostIsOpenAsync((Guid? GroupId, Guid SemesterId, Guid? OwnerUserId, DateTime? ApplicationDeadline, string Status) owner, Guid postId, CancellationToken ct)
    {
        if (!string.Equals(owner.Status, "open", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Recruitment post is not open");

        if (owner.ApplicationDeadline.HasValue && owner.ApplicationDeadline.Value <= DateTime.UtcNow)
        {
            await repo.UpdatePostAsync(postId, null, null, null, "expired", null, ct);
            throw new InvalidOperationException("Recruitment post expired");
        }
    }

    private static void EnsureGroupPost((Guid? GroupId, Guid SemesterId, Guid? OwnerUserId, DateTime? ApplicationDeadline, string Status) owner, bool throwUnauthorized = true)
    {
        if (owner.GroupId is null)
        {
            if (throwUnauthorized)
                throw new UnauthorizedAccessException("Not a group post");
            throw new InvalidOperationException("Post does not accept applications");
        }
    }

    private async Task NotifyLeadersAboutApplicationAsync(Guid postId, Guid groupId, Guid applicantUserId, string? applicantMessage, CancellationToken ct)
    {
        var leaders = await groupQueries.ListActiveMembersAsync(groupId, ct);
        var recipients = leaders
            .Where(m => string.Equals(m.Role, "leader", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(m.Email))
            .ToList();

        if (recipients.Count == 0) return;

        var group = await groupQueries.GetGroupAsync(groupId, ct);
        var groupName = group?.Name ?? "your group";
        var applicant = await _userQueries.GetAdminDetailAsync(applicantUserId, ct);
        var applicantName = applicant?.DisplayName ?? "A student";
        var applicantEmail = applicant?.Email;
        var postDetail = await queries.GetAsync(postId, ExpandOptions.None, null, ct);
        var postTitle = postDetail?.Title ?? groupName;
        var actionUrl = _urlProvider.GetRecruitmentPostUrl(postId);
        var (subject, html) = RecruitmentPostEmailTemplate.BuildApplicationNotice(
            AppName,
            groupName,
            applicantName,
            applicantEmail,
            applicantMessage,
            actionUrl,
            postTitle,
            postDetail?.Description,
            postDetail?.PositionNeeded,
            postDetail?.Skills);

        foreach (var leader in recipients)
        {
            await _emailSender.SendAsync(
                leader.Email,
                subject,
                html,
                ct,
                replyToEmail: applicantEmail,
                fromDisplayName: applicantName);
        }
    }

    private async Task<ApplicationDto> GetApplicationForPostAsync(Guid postId, Guid applicationId, CancellationToken ct)
    {
        var apps = await queries.ListApplicationsAsync(postId, ct);
        var app = apps.FirstOrDefault(a => a.ApplicationId == applicationId);
        if (app is null) throw new KeyNotFoundException("Application not found");
        return app;
    }

    private async Task NotifyApplicantDecisionAsync(
        Guid applicantUserId,
        string? applicantEmail,
        string? applicantDisplayName,
        Guid groupId,
        Guid postId,
        string decision,
        CancellationToken ct)
    {
        var detail = applicantEmail;
        var displayName = applicantDisplayName;
        if (string.IsNullOrWhiteSpace(detail) || string.IsNullOrWhiteSpace(displayName))
        {
            var profile = await _userQueries.GetAdminDetailAsync(applicantUserId, ct);
            detail ??= profile?.Email;
            displayName ??= profile?.DisplayName ?? profile?.Email ?? "You";
        }

        if (string.IsNullOrWhiteSpace(detail)) return;

        var group = await groupQueries.GetGroupAsync(groupId, ct);
        var groupName = group?.Name ?? "your group";
        var postDetail = await queries.GetAsync(postId, ExpandOptions.None, null, ct);
        var actionUrl = _urlProvider.GetRecruitmentPostUrl(postId);
        var (subject, html) = RecruitmentPostEmailTemplate.BuildApplicationDecision(
            AppName,
            displayName ?? "You",
            decision,
            groupName,
            postDetail?.Title ?? groupName,
            postDetail?.Description,
            postDetail?.PositionNeeded,
            postDetail?.Skills,
            actionUrl);

        await _emailSender.SendAsync(
            detail,
            subject,
            html,
            ct,
            fromDisplayName: groupName);
    }

    private static string? SerializeSkills(List<string>? skills)
    {
        if (skills is null || skills.Count == 0) return null;
        var normalized = skills
            .Select(s => s?.Trim())
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (normalized.Count == 0) return null;
        return JsonSerializer.Serialize(normalized);
    }
}
