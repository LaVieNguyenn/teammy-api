using System.Net;

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
        var safeAction = WebUtility.HtmlEncode(actionUrl);
        var safeMessage = string.IsNullOrWhiteSpace(message) ? null : WebUtility.HtmlEncode(message);

        var applicantText = string.IsNullOrWhiteSpace(applicantEmail)
            ? $"<b>{safeApplicant}</b> sent a new application."
            : $@"<b>{safeApplicant}</b> (<a href=""mailto:{safeApplicantEmail}"" style=""color:{brandHex};text-decoration:none;"">{safeApplicantEmail}</a>)
              just applied to join your group.";

        var noteBlock = safeMessage is null
            ? string.Empty
            : $@"<tr>
                   <td style=""padding:16px 24px 0 24px;font-size:14px;color:#0f172a;"">
                     <div style=""font-weight:600;margin-bottom:4px;"">Message</div>
                     <div style=""color:#475569;"">{safeMessage}</div>
                   </td>
                 </tr>";

        var skills = (postSkills is { Count: > 0 }) ? string.Join(", ", postSkills) : null;
        var postBlock = $@"<tr>
            <td style=""padding:16px 24px 0 24px;"">
              <table width=""100%"" style=""border:1px solid #e2e8f0;border-radius:8px;"" cellpadding=""0"" cellspacing=""0"">
                <tr>
                  <td style=""padding:12px 16px;font-size:13px;color:#0f172a;font-weight:600;"">Recruitment post</td>
                </tr>
                <tr>
                  <td style=""padding:12px 16px;border-top:1px solid #e2e8f0;"">
                    <div style=""font-size:15px;color:#0f172a;font-weight:600;"">{System.Net.WebUtility.HtmlEncode(postTitle)}</div>
                    {(string.IsNullOrWhiteSpace(postDescription) ? "" : $@"<div style=""font-size:13px;color:#475569;margin-top:4px;"">{System.Net.WebUtility.HtmlEncode(postDescription)}</div>")}
                    {(string.IsNullOrWhiteSpace(postPosition) ? "" : $@"<div style=""font-size:12px;color:#94a3b8;margin-top:6px;text-transform:uppercase;letter-spacing:1px;"">Position</div><div style=""font-size:13px;color:#334155;"">{System.Net.WebUtility.HtmlEncode(postPosition)}</div>")}
                    {(string.IsNullOrWhiteSpace(skills) ? "" : $@"<div style=""font-size:12px;color:#94a3b8;margin-top:6px;text-transform:uppercase;letter-spacing:1px;"">Skills</div><div style=""font-size:13px;color:#334155;"">{System.Net.WebUtility.HtmlEncode(skills)}</div>")}
                  </td>
                </tr>
              </table>
            </td>
          </tr>";

        var html = $@"<!doctype html>
<html>
  <head>
    <meta name=""viewport"" content=""width=device-width, initial-scale=1"" />
    <meta http-equiv=""Content-Type"" content=""text/html; charset=UTF-8"" />
    <title>{subject}</title>
  </head>
  <body style=""margin:0;background:#f6f9fc;font-family:Segoe UI,Arial,Helvetica,sans-serif;color:#0f172a;"">
    <table role=""presentation"" border=""0"" cellpadding=""0"" cellspacing=""0"" width=""100%"">
      <tr><td align=""center"" style=""padding:24px;"">
        <table role=""presentation"" width=""640"" style=""max-width:640px;background:#ffffff;border-radius:12px;border:1px solid #e2e8f0;"" cellpadding=""0"" cellspacing=""0"">
          <tr>
            <td style=""padding:24px;font-size:18px;font-weight:600;color:#0f172a;"">
              New applicant for {safeGroup}
            </td>
          </tr>
          <tr>
            <td style=""padding:0 24px 12px 24px;font-size:14px;color:#475569;"">{applicantText}</td>
          </tr>
          {noteBlock}
          {postBlock}
          <tr>
            <td style=""padding:16px 24px 0 24px;"">
              <a href=""{safeAction}"" style=""background:{brandHex};color:#ffffff;text-decoration:none;padding:12px 18px;border-radius:8px;display:inline-block;font-weight:600;"">
                Review application
              </a>
            </td>
          </tr>
          <tr>
            <td style=""padding:16px 24px 24px 24px;font-size:12px;color:#94a3b8;"">
              © {DateTime.UtcNow:yyyy} {appName}. All rights reserved.
            </td>
          </tr>
        </table>
      </td></tr>
    </table>
  </body>
</html>";

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
        var safeAction = WebUtility.HtmlEncode(actionUrl);

        var statusLine = normalizedDecision == "accepted"
            ? "Congratulations! Your application has been accepted."
            : "Unfortunately, your application was not selected.";

        var html = $@"<!doctype html>
<html>
  <head>
    <meta name=""viewport"" content=""width=device-width, initial-scale=1"" />
    <meta http-equiv=""Content-Type"" content=""text/html; charset=UTF-8"" />
    <title>{subject}</title>
  </head>
  <body style=""margin:0;background:#f6f9fc;font-family:Segoe UI,Arial,Helvetica,sans-serif;color:#0f172a;"">
    <table role=""presentation"" border=""0"" cellpadding=""0"" cellspacing=""0"" width=""100%"">
      <tr><td align=""center"" style=""padding:24px;"">
        <table role=""presentation"" width=""640"" style=""max-width:640px;background:#ffffff;border-radius:12px;border:1px solid #e2e8f0;"" cellpadding=""0"" cellspacing=""0"">
          <tr>
            <td style=""padding:24px;font-size:18px;font-weight:600;color:#0f172a;"">
              Application update from {safeGroup}
            </td>
          </tr>
          <tr>
            <td style=""padding:0 24px 12px 24px;font-size:14px;color:#475569;"">
              Hi {safeApplicant},<br/>{statusLine}
            </td>
          </tr>
          <tr>
            <td style=""padding:12px 24px 0 24px;"">
              <div style=""font-size:13px;color:#94a3b8;text-transform:uppercase;letter-spacing:1px;"">Recruitment post</div>
              <div style=""font-size:16px;color:#0f172a;font-weight:600;margin-top:4px;"">{safeTitle}</div>
              {(safeDescription is null ? string.Empty : $@"<div style=""font-size:13px;color:#475569;margin-top:4px;"">{safeDescription}</div>")}
              {(safePosition is null ? string.Empty : $@"<div style=""font-size:12px;color:#94a3b8;margin-top:10px;text-transform:uppercase;letter-spacing:1px;"">Position</div><div style=""font-size:13px;color:#334155;"">{safePosition}</div>")}
              {(safeSkills is null ? string.Empty : $@"<div style=""font-size:12px;color:#94a3b8;margin-top:10px;text-transform:uppercase;letter-spacing:1px;"">Skills</div><div style=""font-size:13px;color:#334155;"">{safeSkills}</div>")}
            </td>
          </tr>
          <tr>
            <td style=""padding:16px 24px;"">
              <a href=""{safeAction}"" style=""background:{brandHex};color:#ffffff;text-decoration:none;padding:12px 18px;border-radius:8px;display:inline-block;font-weight:600;"">
                View details
              </a>
            </td>
          </tr>
          <tr>
            <td style=""padding:0 24px 24px 24px;font-size:12px;color:#94a3b8;"">
              © {DateTime.UtcNow:yyyy} {appName}. All rights reserved.
            </td>
          </tr>
        </table>
      </td></tr>
    </table>
  </body>
</html>";

        return (subject, html);
    }
}
