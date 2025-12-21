using Teammy.Application.Common.Email;

namespace Teammy.Application.Invitations.Templates;

public static class InvitationEmailTemplate
{
    public static (string Subject, string Html) Build(
        string appName,
        string leaderName,
        string leaderEmail,
        string groupName,
        string actionUrl,
        string? logoUrl = null,
        string brandHex = "#F97316",
        IEnumerable<(string Label, string Value)>? extraInfo = null,
        string? extraTitle = null)
    {
        var subject = $"{appName} - {leaderName} invited you to {groupName}";
        var safeLeader = System.Net.WebUtility.HtmlEncode(leaderName);
        var safeLeaderEmail = System.Net.WebUtility.HtmlEncode(leaderEmail);
        var safeGroup = System.Net.WebUtility.HtmlEncode(groupName);

        var extraRows = extraInfo?
            .Where(p => !string.IsNullOrWhiteSpace(p.Value))
            .Select(p => (Label: System.Net.WebUtility.HtmlEncode(p.Label), Value: System.Net.WebUtility.HtmlEncode(p.Value)))
            .ToList();

        var extraBlock = (extraRows is { Count: > 0 })
            ? $@"<div style=""margin-top:12px;"">
  <table width=""100%"" style=""border:1px solid #e2e8f0;border-radius:10px;"" cellpadding=""0"" cellspacing=""0"">
    <tr>
      <td style=""padding:12px 16px;font-size:13px;color:#0f172a;font-weight:700;"">{System.Net.WebUtility.HtmlEncode(extraTitle ?? "Post details")}</td>
    </tr>
    {string.Join(string.Empty, extraRows.Select(row => $@"<tr>
      <td style=""padding:12px 16px;border-top:1px solid #e2e8f0;font-size:14px;color:#334155;"">
        <div style=""font-size:12px;text-transform:uppercase;letter-spacing:1px;color:#94a3b8;margin-bottom:4px;"">{row.Label}</div>
        <div>{row.Value}</div>
      </td>
    </tr>"))}
  </table>
</div>"
            : string.Empty;

        var messageHtml = $@"<div style=""font-size:15px;line-height:22px;color:#0f172a;"">
  <b>{safeLeader}</b> (<a href=""mailto:{safeLeaderEmail}"" style=""color:{brandHex};text-decoration:none;"">{safeLeaderEmail}</a>) has invited you to join the <b>{safeGroup}</b> group.
</div>
<div style=""margin-top:12px;"">
  <table width=""100%"" style=""border:1px solid #e2e8f0;border-radius:10px;"" cellpadding=""0"" cellspacing=""0"">
    <tr>
      <td style=""padding:12px 16px;font-size:13px;color:#0f172a;font-weight:700;"">Group</td>
    </tr>
    <tr>
      <td style=""padding:12px 16px;border-top:1px solid #e2e8f0;font-size:14px;color:#334155;"">{safeGroup}</td>
    </tr>
  </table>
</div>
<div style=""margin-top:12px;font-size:14px;color:#475569;"">You'll be joining the {safeGroup} team to collaborate and access all group resources.</div>
{extraBlock}";

        var html = EmailTemplateBuilder.Build(
            subject,
            "Group invitation",
            messageHtml,
            "View Invitation",
            actionUrl);

        return (subject, html);
    }

    public static (string Subject, string Html) BuildMentorInvite(
        string appName,
        string leaderName,
        string leaderEmail,
        string groupName,
        string topicTitle,
        string? topicDescription,
        string actionUrl,
        string? note,
        string? logoUrl = null,
        string brandHex = "#2563EB")
    {
        var subject = $"{appName} - {leaderName} requests you as mentor for {groupName}";
        var safeLeader = System.Net.WebUtility.HtmlEncode(leaderName);
        var safeLeaderEmail = System.Net.WebUtility.HtmlEncode(leaderEmail);
        var safeGroup = System.Net.WebUtility.HtmlEncode(groupName);
        var safeTopic = System.Net.WebUtility.HtmlEncode(topicTitle);
        var safeDesc = System.Net.WebUtility.HtmlEncode(topicDescription ?? "No description provided");
        var safeNote = string.IsNullOrWhiteSpace(note) ? null : System.Net.WebUtility.HtmlEncode(note);

        var noteBlock = safeNote is null
            ? string.Empty
            : $@"<div style=""margin-top:12px;font-size:14px;color:#0f172a;"">
  <div style=""font-weight:700;margin-bottom:4px;"">Message from {safeLeader}</div>
  <div style=""color:#334155;"">{safeNote}</div>
</div>";

        var messageHtml = $@"<div style=""font-size:14px;color:#475569;"">
  <b>{safeLeader}</b> (<a href=""mailto:{safeLeaderEmail}"" style=""color:{brandHex};text-decoration:none;"">{safeLeaderEmail}</a>)
  is requesting you to mentor the group <b>{safeGroup}</b> on topic <b>{safeTopic}</b>.
</div>
<div style=""margin-top:12px;"">
  <table width=""100%"" style=""border:1px solid #e2e8f0;border-radius:10px;"" cellpadding=""0"" cellspacing=""0"">
    <tr>
      <td style=""padding:12px 16px;font-size:13px;color:#475569;text-transform:uppercase;letter-spacing:1px;"">Topic</td>
    </tr>
    <tr>
      <td style=""padding:12px 16px;border-top:1px solid #e2e8f0;"">
        <div style=""font-size:15px;color:#0f172a;font-weight:700;"">{safeTopic}</div>
        <div style=""font-size:13px;color:#475569;margin-top:4px;"">{safeDesc}</div>
      </td>
    </tr>
  </table>
</div>
{noteBlock}";

        var html = EmailTemplateBuilder.Build(
            subject,
            $"Mentor invitation for {safeGroup}",
            messageHtml,
            "Review invitation",
            actionUrl);

        return (subject, html);
    }
}
