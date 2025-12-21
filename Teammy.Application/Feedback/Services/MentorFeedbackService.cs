using System.Text;
using Teammy.Application.Activity.Dtos;
using Teammy.Application.Activity.Services;
using Teammy.Application.Common.Email;
using Teammy.Application.Common.Interfaces;
using Teammy.Application.Feedback.Dtos;
using Teammy.Application.Groups.Dtos;
using Teammy.Application.Kanban.Interfaces;

namespace Teammy.Application.Feedback.Services;

public sealed class MentorFeedbackService(
    IGroupReadOnlyQueries groupQueries,
    IGroupAccessQueries groupAccess,
    IGroupFeedbackRepository feedbackRepository,
    IGroupFeedbackReadOnlyQueries feedbackQueries,
    IUserReadOnlyQueries userQueries,
    IEmailSender emailSender,
    ActivityLogService activityLog)
{
    private const string DefaultAppUrl = "https://teammy.vercel.app/login";

    private static readonly HashSet<string> AllowedStatusUpdates = new(StringComparer.OrdinalIgnoreCase)
    {
        "acknowledged",
        "follow_up_requested",
        "resolved"
    };

    public async Task<Guid> SubmitAsync(Guid groupId, Guid mentorUserId, SubmitGroupFeedbackRequest req, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(req);
        if (string.IsNullOrWhiteSpace(req.Summary))
            throw new ArgumentException("Summary is required");
        if (req.Rating.HasValue && (req.Rating < 1 || req.Rating > 5))
            throw new ArgumentException("Rating must be between 1 and 5");

        var group = await groupQueries.GetGroupAsync(groupId, ct) ?? throw new KeyNotFoundException("Group not found");
        if (!await groupAccess.IsMentorAsync(groupId, mentorUserId, ct))
            throw new UnauthorizedAccessException("Only assigned mentor can submit feedback");

        var createModel = new GroupFeedbackCreateModel(
            groupId,
            group.SemesterId,
            mentorUserId,
            req.Category?.Trim(),
            req.Summary.Trim(),
            req.Details?.Trim(),
            req.Rating,
            req.Blockers?.Trim(),
            req.NextSteps?.Trim());

        var feedbackId = await feedbackRepository.CreateAsync(createModel, ct);
        await activityLog.LogAsync(new ActivityLogCreateRequest(mentorUserId, "group", "MENTOR_FEEDBACK_SUBMITTED")
        {
            GroupId = groupId,
            EntityId = feedbackId,
            Message = $"Mentor submitted feedback ({req.Category ?? "general"})",
            Metadata = new { groupId, feedbackId }
        }, ct);

        await NotifyLeadersAsync(groupId, group.Name, mentorUserId, req, ct);
        return feedbackId;
    }

    public async Task<IReadOnlyList<GroupFeedbackDto>> ListAsync(Guid groupId, Guid currentUserId, CancellationToken ct)
    {
        _ = await groupQueries.GetGroupAsync(groupId, ct) ?? throw new KeyNotFoundException("Group not found");
        var isMember = await groupAccess.IsMemberAsync(groupId, currentUserId, ct);
        var isLeader = await groupAccess.IsLeaderAsync(groupId, currentUserId, ct);
        var isMentor = await groupAccess.IsMentorAsync(groupId, currentUserId, ct);
        if (!isLeader && !isMentor && !isMember)
            throw new UnauthorizedAccessException("Only members, leaders, or mentors can view mentor feedback");

        var list = await feedbackQueries.ListForGroupAsync(groupId, ct);
        return list;
    }

    public async Task UpdateStatusAsync(Guid groupId, Guid feedbackId, Guid leaderUserId, UpdateGroupFeedbackStatusRequest req, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(req);
        if (!AllowedStatusUpdates.Contains(req.Status))
            throw new ArgumentException("Unsupported status. Allowed: acknowledged, follow_up_requested, resolved");

        var group = await groupQueries.GetGroupAsync(groupId, ct) ?? throw new KeyNotFoundException("Group not found");
        if (!await groupAccess.IsLeaderAsync(groupId, leaderUserId, ct))
            throw new UnauthorizedAccessException("Leader only");

        var feedback = await feedbackQueries.GetAsync(feedbackId, ct) ?? throw new KeyNotFoundException("Feedback not found");
        if (feedback.GroupId != groupId)
            throw new InvalidOperationException("Feedback does not belong to this group");

        await feedbackRepository.UpdateStatusAsync(feedbackId, req.Status.ToLowerInvariant(), leaderUserId, req.Note?.Trim(), ct);

        await activityLog.LogAsync(new ActivityLogCreateRequest(leaderUserId, "group", "MENTOR_FEEDBACK_UPDATED")
        {
            GroupId = groupId,
            EntityId = feedbackId,
            Message = $"Leader set mentor feedback to {req.Status}",
            Metadata = new { groupId, feedbackId, req.Note }
        }, ct);

        if (!string.IsNullOrWhiteSpace(feedback.MentorEmail))
        {
            var note = string.IsNullOrWhiteSpace(req.Note) ? "No additional note." : req.Note;
            var messageHtml = $@"<p>The leader updated your feedback for <strong>{System.Net.WebUtility.HtmlEncode(group.Name)}</strong>.</p>
<p style=""margin-top:8px;"">New status: <strong>{System.Net.WebUtility.HtmlEncode(req.Status)}</strong></p>
<p style=""margin-top:8px;"">Note: {System.Net.WebUtility.HtmlEncode(note)}</p>";
            var html = EmailTemplateBuilder.Build(
                $"TEAMMY - Feedback updated ({req.Status})",
                "Feedback status updated",
                messageHtml,
                "Open Teammy",
                DefaultAppUrl);
            await emailSender.SendAsync(
                feedback.MentorEmail!,
                $"TEAMMY - Feedback updated ({req.Status})",
                html,
                ct);
        }
    }

    public async Task UpdateAsync(Guid groupId, Guid feedbackId, Guid mentorUserId, UpdateGroupFeedbackRequest req, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(req);

        var group = await groupQueries.GetGroupAsync(groupId, ct) ?? throw new KeyNotFoundException("Group not found");
        if (!await groupAccess.IsMentorAsync(groupId, mentorUserId, ct))
            throw new UnauthorizedAccessException("Mentor only");

        var feedback = await feedbackQueries.GetAsync(feedbackId, ct) ?? throw new KeyNotFoundException("Feedback not found");
        if (feedback.GroupId != groupId)
            throw new InvalidOperationException("Feedback does not belong to this group");
        if (feedback.MentorId != mentorUserId)
            throw new UnauthorizedAccessException("Cannot edit another mentor's feedback");
        if (string.Equals(feedback.Status, "resolved", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Feedback already resolved");

        var summary = req.Summary is null ? null : req.Summary.Trim();
        if (summary is not null && string.IsNullOrWhiteSpace(summary))
            throw new ArgumentException("Summary cannot be empty");
        if (req.Rating.HasValue && (req.Rating < 1 || req.Rating > 5))
            throw new ArgumentException("Rating must be between 1 and 5");

        var updateModel = new GroupFeedbackUpdateModel(
            req.Category?.Trim(),
            summary,
            req.Details,
            req.Rating,
            req.Blockers,
            req.NextSteps);

        await feedbackRepository.UpdateAsync(feedbackId, updateModel, ct);
    }

    public async Task DeleteAsync(Guid groupId, Guid feedbackId, Guid mentorUserId, CancellationToken ct)
    {
        var group = await groupQueries.GetGroupAsync(groupId, ct) ?? throw new KeyNotFoundException("Group not found");
        if (!await groupAccess.IsMentorAsync(groupId, mentorUserId, ct))
            throw new UnauthorizedAccessException("Mentor only");

        var feedback = await feedbackQueries.GetAsync(feedbackId, ct) ?? throw new KeyNotFoundException("Feedback not found");
        if (feedback.GroupId != groupId)
            throw new InvalidOperationException("Feedback does not belong to this group");
        if (feedback.MentorId != mentorUserId)
            throw new UnauthorizedAccessException("Cannot delete another mentor's feedback");
        if (!string.Equals(feedback.Status, "submitted", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Only submitted feedback can be deleted");

        await feedbackRepository.DeleteAsync(feedbackId, ct);
    }

    private async Task NotifyLeadersAsync(Guid groupId, string groupName, Guid mentorUserId, SubmitGroupFeedbackRequest req, CancellationToken ct)
    {
        var members = await groupQueries.ListActiveMembersAsync(groupId, ct);
        var leaders = members.Where(m => string.Equals(m.Role, "leader", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(m.Email)).ToList();
        if (leaders.Count == 0) return;

        var mentor = await userQueries.GetCurrentUserAsync(mentorUserId, ct);
        var mentorName = mentor?.DisplayName ?? "Mentor";
        var summary = req.Summary.Length > 200 ? req.Summary[..200] + "..." : req.Summary;

        var sb = new StringBuilder();
        sb.AppendLine($"<p><strong>{System.Net.WebUtility.HtmlEncode(mentorName)}</strong> submitted feedback for <strong>{System.Net.WebUtility.HtmlEncode(groupName)}</strong>.</p>");
        sb.AppendLine($"<p><em>{System.Net.WebUtility.HtmlEncode(summary)}</em></p>");
        if (!string.IsNullOrWhiteSpace(req.Blockers))
        {
            sb.AppendLine("<p><strong>Blockers:</strong></p>");
            sb.AppendLine($"<p>{System.Net.WebUtility.HtmlEncode(req.Blockers)}</p>");
        }
        var html = EmailTemplateBuilder.Build(
            "TEAMMY - Mentor submitted feedback",
            "Mentor feedback received",
            sb.ToString(),
            "Open Teammy",
            DefaultAppUrl);

        foreach (var leader in leaders)
        {
            await emailSender.SendAsync(
                leader.Email!,
                "TEAMMY - Mentor submitted feedback",
                html,
                ct);
        }
    }
}
