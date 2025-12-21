using System.Net;
using Teammy.Application.Common.Email;

namespace Teammy.Application.Posts.Templates;

public static class RecruitmentPostEmailTemplate
{
    public static (string Subject, string Html) BuildApplicationNotice(
        string appName,
        string groupName,
        string applicantName,
        string? applicantEmail,
        string? message,
        string actionUrl,
        string postTitle,
        string? postDescription,
        string? postPosition,
        IReadOnlyList<string>? postSkills,
        string brandHex = "#2563EB")
    {
        var subject = $"{appName} - New applicant for {groupName}";
        var safeGroup = WebUtility.HtmlEncode(groupName);
        var safeApplicant = WebUtility.HtmlEncode(applicantName);
        var safeApplicantEmail = WebUtility.HtmlEncode(applicantEmail ?? string.Empty);
        var safeMessage = string.IsNullOrWhiteSpace(message) ? null : WebUtility.HtmlEncode(message);

        var applicantText = string.IsNullOrWhiteSpace(applicantEmail)
            ? $"<b>{safeApplicant}</b> sent a new application."
            : $@"<b>{safeApplicant}</b> (<a href=""mailto:{safeApplicantEmail}"" style=""color:{brandHex};text-decoration:none;"">{safeApplicantEmail}</a>)
just applied to join your group.";

        var noteBlock = safeMessage is null
            ? string.Empty
            : $@"<div style=""margin-top:12px;font-size:14px;color:#0f172a;"">
  <div style=""font-weight:700;margin-bottom:4px;"">Message</div>
  <div style=""color:#475569;"">{safeMessage}</div>
</div>";

        var skills = (postSkills is { Count: > 0 }) ? string.Join(", ", postSkills) : null;
        var postBlock = $@"<div style=""margin-top:12px;"">
  <table width=""100%"" style=""border:1px solid #e2e8f0;border-radius:10px;"" cellpadding=""0"" cellspacing=""0"">
    <tr>
      <td style=""padding:12px 16px;font-size:13px;color:#0f172a;font-weight:700;"">Recruitment post</td>
    </tr>
    <tr>
      <td style=""padding:12px 16px;border-top:1px solid #e2e8f0;"">
        <div style=""font-size:15px;color:#0f172a;font-weight:700;"">{WebUtility.HtmlEncode(postTitle)}</div>
        {(string.IsNullOrWhiteSpace(postDescription) ? "" : $@"<div style=""font-size:13px;color:#475569;margin-top:4px;"">{WebUtility.HtmlEncode(postDescription)}</div>")}
        {(string.IsNullOrWhiteSpace(postPosition) ? "" : $@"<div style=""font-size:12px;color:#94a3b8;margin-top:6px;text-transform:uppercase;letter-spacing:1px;"">Position</div><div style=""font-size:13px;color:#334155;"">{WebUtility.HtmlEncode(postPosition)}</div>")}
        {(string.IsNullOrWhiteSpace(skills) ? "" : $@"<div style=""font-size:12px;color:#94a3b8;margin-top:6px;text-transform:uppercase;letter-spacing:1px;"">Skills</div><div style=""font-size:13px;color:#334155;"">{WebUtility.HtmlEncode(skills)}</div>")}
      </td>
    </tr>
  </table>
</div>";

        var messageHtml = $@"<div style=""font-size:15px;color:#0f172a;"">{applicantText}</div>
{noteBlock}
{postBlock}";

        var html = EmailTemplateBuilder.Build(
            subject,
            $"New applicant for {safeGroup}",
            messageHtml,
            "Review application",
            actionUrl);

        return (subject, html);
    }

    public static (string Subject, string Html) BuildApplicationDecision(
        string appName,
        string applicantName,
        string decision,
        string groupName,
        string postTitle,
        string? postDescription,
        string? postPosition,
        IReadOnlyList<string>? postSkills,
        string actionUrl,
        string brandHex = "#2563EB")
    {
        var normalizedDecision = string.Equals(decision, "accepted", StringComparison.OrdinalIgnoreCase)
            ? "accepted"
            : "rejected";

        var subject = $"{appName} - Your application was {normalizedDecision}";
        var safeApplicant = WebUtility.HtmlEncode(applicantName);
        var safeGroup = WebUtility.HtmlEncode(groupName);
        var safeTitle = WebUtility.HtmlEncode(postTitle);
        var safeDescription = string.IsNullOrWhiteSpace(postDescription) ? null : WebUtility.HtmlEncode(postDescription);
        var safePosition = string.IsNullOrWhiteSpace(postPosition) ? null : WebUtility.HtmlEncode(postPosition);
        var safeSkills = (postSkills is { Count: > 0 }) ? WebUtility.HtmlEncode(string.Join(", ", postSkills)) : null;

        var statusLine = normalizedDecision == "accepted"
            ? "Congratulations! Your application has been accepted."
            : "Unfortunately, your application was not selected.";

        var messageHtml = $@"<div style=""font-size:14px;color:#475569;"">
  Hi {safeApplicant},<br/>{statusLine}
</div>
<div style=""margin-top:12px;"">
  <div style=""font-size:12px;color:#94a3b8;text-transform:uppercase;letter-spacing:1px;"">Recruitment post</div>
  <div style=""font-size:16px;color:#0f172a;font-weight:700;margin-top:4px;"">{safeTitle}</div>
  {(safeDescription is null ? string.Empty : $@"<div style=""font-size:13px;color:#475569;margin-top:4px;"">{safeDescription}</div>")}
  {(safePosition is null ? string.Empty : $@"<div style=""font-size:12px;color:#94a3b8;margin-top:10px;text-transform:uppercase;letter-spacing:1px;"">Position</div><div style=""font-size:13px;color:#334155;"">{safePosition}</div>")}
  {(safeSkills is null ? string.Empty : $@"<div style=""font-size:12px;color:#94a3b8;margin-top:10px;text-transform:uppercase;letter-spacing:1px;"">Skills</div><div style=""font-size:13px;color:#334155;"">{safeSkills}</div>")}
</div>";

        var html = EmailTemplateBuilder.Build(
            subject,
            $"Application update from {safeGroup}",
            messageHtml,
            "View details",
            actionUrl);

        return (subject, html);
    }
}
