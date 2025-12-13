using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Text;
using Teammy.Application.Announcements.Dtos;
using Teammy.Application.Common.Dtos;
using Teammy.Application.Common.Interfaces;

namespace Teammy.Application.Announcements.Services;

public sealed class AnnouncementService(
    IAnnouncementRepository repository,
    IAnnouncementReadOnlyQueries readQueries,
    IAnnouncementRecipientQueries recipientQueries,
    IEmailSender emailSender,
    IAnnouncementNotifier notifier
)
{
    private const int MaxPreviewPageSize = 200;

    public Task<IReadOnlyList<AnnouncementDto>> ListAsync(Guid currentUserId, AnnouncementFilter filter, CancellationToken ct)
        => readQueries.ListForUserAsync(currentUserId, filter, ct);

    public Task<AnnouncementDto?> GetAsync(Guid announcementId, Guid currentUserId, CancellationToken ct)
        => readQueries.GetForUserAsync(announcementId, currentUserId, ct);

    public async Task<AnnouncementDto> CreateAsync(Guid currentUserId, CreateAnnouncementRequest request, CancellationToken ct)
    {
        if (request is null)
            throw new ArgumentNullException(nameof(request));

        var normalizedScope = NormalizeScope(request.Scope);
        ValidateRequest(normalizedScope, request);

        var publishAt = (request.PublishAt ?? DateTime.UtcNow).ToUniversalTime();
        DateTime? expireAt = request.ExpireAt?.ToUniversalTime();
        if (expireAt.HasValue && expireAt.Value <= publishAt)
            throw new ValidationException("ExpireAt must be later than PublishAt");

        var title = (request.Title ?? string.Empty).Trim();
        var content = (request.Content ?? string.Empty).Trim();

        var command = new CreateAnnouncementCommand(
            currentUserId,
            request.SemesterId,
            normalizedScope,
            title,
            content,
            NormalizeRole(request.TargetRole),
            request.TargetGroupId,
            publishAt,
            expireAt,
            request.Pinned
        );

        var created = await repository.CreateAsync(command, ct);
        var recipients = await ResolveRecipientsAsync(created, ct);

        if (recipients.Count > 0)
        {
            await notifier.NotifyCreatedAsync(created, recipients, ct);
            await SendEmailsAsync(created, recipients, ct);
        }

        return created;
    }

    public async Task<AnnouncementRecipientPreviewDto> PreviewRecipientsAsync(Guid currentUserId, AnnouncementRecipientPreviewRequest request, CancellationToken ct)
    {
        if (request is null)
            throw new ArgumentNullException(nameof(request));

        var scope = NormalizeScope(request.Scope);
        ValidateScopeFilters(scope, request.SemesterId, request.TargetRole, request.TargetGroupId);
        var normalizedRole = NormalizeRole(request.TargetRole);
        var (page, pageSize) = NormalizePagination(request.Page, request.PageSize);

        var recipients = await recipientQueries.ListRecipientsAsync(scope, request.SemesterId, normalizedRole, request.TargetGroupId, page, pageSize, ct);
        return new AnnouncementRecipientPreviewDto(scope, request.SemesterId, normalizedRole, request.TargetGroupId, recipients);
    }

    private static string NormalizeScope(string scope)
    {
        if (!AnnouncementScopes.IsValid(scope))
            throw new ValidationException("Invalid scope value");
        return scope.Trim().ToLowerInvariant();
    }

    private static string? NormalizeRole(string? role)
    {
        if (string.IsNullOrWhiteSpace(role))
            return null;
        var normalized = role.Trim().ToLowerInvariant();
        if (!AnnouncementRoles.IsValid(normalized))
            throw new ValidationException("Invalid target role value");
        return normalized;
    }

    private static void ValidateRequest(string scope, CreateAnnouncementRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Title))
            throw new ValidationException("Title is required");
        if (string.IsNullOrWhiteSpace(request.Content))
            throw new ValidationException("Content is required");

        ValidateScopeFilters(scope, request.SemesterId, request.TargetRole, request.TargetGroupId);
    }

    private static void ValidateScopeFilters(string scope, Guid? semesterId, string? targetRole, Guid? targetGroupId)
    {
        switch (scope)
        {
            case AnnouncementScopes.Semester:
            case AnnouncementScopes.GroupsWithoutTopic:
            case AnnouncementScopes.GroupsUnderstaffed:
            case AnnouncementScopes.StudentsWithoutGroup:
                if (!semesterId.HasValue)
                    throw new ValidationException("SemesterId is required for this scope");
                break;
            case AnnouncementScopes.Role:
                if (string.IsNullOrWhiteSpace(targetRole))
                    throw new ValidationException("TargetRole is required for role scope");
                break;
            case AnnouncementScopes.Group:
                if (!targetGroupId.HasValue)
                    throw new ValidationException("TargetGroupId is required for group scope");
                break;
        }
    }

    private static (int Page, int PageSize) NormalizePagination(int page, int pageSize)
    {
        if (page <= 0)
            page = 1;
        if (pageSize <= 0)
            pageSize = 25;
        return (page, Math.Min(pageSize, MaxPreviewPageSize));
    }

    private Task<IReadOnlyList<AnnouncementRecipient>> ResolveRecipientsAsync(AnnouncementDto announcement, CancellationToken ct)
    {
        return recipientQueries.ResolveRecipientsAsync(
            announcement.Scope,
            announcement.SemesterId,
            announcement.TargetRole,
            announcement.TargetGroupId,
            ct);
    }

    private async Task SendEmailsAsync(AnnouncementDto announcement, IReadOnlyList<AnnouncementRecipient> recipients, CancellationToken ct)
    {
        var subject = $"[Teammy] {announcement.Title}";
        var htmlBody = BuildHtmlBody(announcement);

        foreach (var recipient in recipients)
        {
            try
            {
                await emailSender.SendAsync(
                    recipient.Email,
                    subject,
                    htmlBody,
                    ct,
                    replyToEmail: null,
                    fromDisplayName: "Teammy");
            }
            catch
            {
                // ignore single-recipient failure to avoid breaking the API flow
            }
        }
    }

    private static string BuildHtmlBody(AnnouncementDto announcement)
    {
        var builder = new StringBuilder();
        builder.Append("<div style=\"font-family:Arial,Helvetica,sans-serif;font-size:14px;color:#0f172a;\">");
        builder.AppendFormat("<h2 style=\"color:#2563EB;margin-bottom:12px;\">{0}</h2>", WebUtility.HtmlEncode(announcement.Title));
        builder.AppendFormat("<p style=\"margin-bottom:16px;color:#475569;\"><strong>Published:</strong> {0:dd MMM yyyy HH:mm} UTC</p>", announcement.PublishAt);
        if (!string.IsNullOrWhiteSpace(announcement.CreatedByName))
        {
            builder.AppendFormat("<p style=\"margin-bottom:16px;color:#475569;\"><strong>By:</strong> {0}</p>", WebUtility.HtmlEncode(announcement.CreatedByName));
        }

        builder.Append("<div style=\"line-height:1.6;white-space:pre-line;\">");
        builder.Append(WebUtility.HtmlEncode(announcement.Content));
        builder.Append("</div>");

        builder.Append("<hr style=\"margin:24px 0;border:none;border-top:1px solid #e2e8f0;\"/>");
        builder.Append("<p style=\"color:#94a3b8;font-size:12px;\">Bạn nhận được thông báo này từ hệ thống Teammy.</p>");
        builder.Append("</div>");
        return builder.ToString();
    }
}
