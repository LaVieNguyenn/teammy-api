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
        var safeAction = System.Net.WebUtility.HtmlEncode(actionUrl);

        var extraRows = extraInfo?
            .Where(p => !string.IsNullOrWhiteSpace(p.Value))
            .Select(p => (Label: System.Net.WebUtility.HtmlEncode(p.Label), Value: System.Net.WebUtility.HtmlEncode(p.Value)))
            .ToList();

        var extraBlock = (extraRows is { Count: > 0 })
            ? $@"<tr>
            <td style=""padding:16px 24px 0 24px;"">
              <table width=""100%"" style=""border:1px solid #e2e8f0;border-radius:8px;"" cellpadding=""0"" cellspacing=""0"">
                <tr>
                  <td style=""padding:12px 16px;font-size:13px;color:#0f172a;font-weight:600;"">{System.Net.WebUtility.HtmlEncode(extraTitle ?? "Post details")}</td>
                </tr>
                {string.Join(string.Empty, extraRows.Select(row => $@"<tr>
                  <td style=""padding:12px 16px;border-top:1px solid #e2e8f0;font-size:14px;color:#334155;"">
                    <div style=""font-size:12px;text-transform:uppercase;letter-spacing:1px;color:#94a3b8;margin-bottom:4px;"">{row.Label}</div>
                    <div>{row.Value}</div>
                  </td>
                </tr>"))}
              </table>
            </td>
          </tr>"
            : string.Empty;

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
            <td style=""padding:8px 24px 0 24px;"">
              <div style=""font-size:16px;line-height:24px;color:#0f172a;"">
                <b>{safeLeader}</b> (<a href=""mailto:{safeLeaderEmail}"" style=""color:{brandHex};text-decoration:none;"">{safeLeaderEmail}</a>) has invited you to join the <b>{safeGroup}</b> group.
              </div>
            </td>
          </tr>
          <tr>
            <td style=""padding:16px 24px 0 24px;"">
              <table width=""100%"" style=""border:1px solid #e2e8f0;border-radius:8px;"" cellpadding=""0"" cellspacing=""0"">
                <tr>
                  <td style=""padding:12px 16px;font-size:13px;color:#0f172a;font-weight:600;"">Group</td>
                </tr>
                <tr>
                  <td style=""padding:12px 16px;border-top:1px solid #e2e8f0;font-size:14px;color:#334155;"">{safeGroup}</td>
                </tr>
              </table>
            </td>
          </tr>
          <tr>
            <td style=""padding:20px 24px 0 24px;"">
              <div style=""font-size:14px;color:#334155;"">You'll be joining the {safeGroup} team to collaborate and access all group resources.</div>
            </td>
          </tr>
          {extraBlock}
          <tr>
            <td style=""padding:24px;"">
              <a href=""{safeAction}"" style=""background:{brandHex};color:#ffffff;text-decoration:none;padding:12px 18px;border-radius:8px;display:inline-block;font-weight:600;"">View Invitation</a>
            </td>
          </tr>
          <tr>
            <td style=""padding:16px 24px 24px 24px;"">
              <div style=""height:1px;background:#e2e8f0;""></div>
              <div style=""font-size:12px;color:#64748b;padding-top:12px;"">&copy; {DateTime.UtcNow:yyyy} {appName}. All rights reserved.</div>
            </td>
          </tr>
        </table>
      </td></tr>
    </table>
  </body>
</html>";

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
        var safeAction = System.Net.WebUtility.HtmlEncode(actionUrl);
        var safeNote = string.IsNullOrWhiteSpace(note) ? null : System.Net.WebUtility.HtmlEncode(note);

        var noteBlock = safeNote is null
            ? string.Empty
            : $@"<tr>
                  <td style=""padding:16px 24px 0 24px;font-size:14px;color:#0f172a;"">
                    <div style=""font-weight:600;margin-bottom:4px;"">Message from {safeLeader}</div>
                    <div style=""color:#334155;"">{safeNote}</div>
                  </td>
                </tr>";

        var html = $@"<!doctype html>
<html>
  <head>
    <meta name=""viewport"" content=""width=device-width, initial-scale=1"" />
    <meta http-equiv=""Content-Type"" content=""text/html; charset=UTF-8"" />
    <title>{subject}</title>
  </head>
  <body style=""margin:0;background:#f2f6fc;font-family:Segoe UI,Arial,Helvetica,sans-serif;color:#0f172a;"">
    <table role=""presentation"" border=""0"" cellpadding=""0"" cellspacing=""0"" width=""100%"">
      <tr><td align=""center"" style=""padding:24px;"">
        <table role=""presentation"" width=""640"" style=""max-width:640px;background:#ffffff;border-radius:12px;border:1px solid #dbe3f0;"" cellpadding=""0"" cellspacing=""0"">
          <tr>
            <td style=""padding:24px 24px 8px 24px;font-size:18px;font-weight:600;color:#0f172a;"">
              Mentor invitation for {safeGroup}
            </td>
          </tr>
          <tr>
            <td style=""padding:0 24px 12px 24px;color:#475569;font-size:14px;"">
              <b>{safeLeader}</b> (<a href=""mailto:{safeLeaderEmail}"" style=""color:{brandHex};text-decoration:none;"">{safeLeaderEmail}</a>)
              is requesting you to mentor the group <b>{safeGroup}</b> on topic <b>{safeTopic}</b>.
            </td>
          </tr>
          <tr>
            <td style=""padding:0 24px 16px 24px;"">
              <table width=""100%"" style=""border:1px solid #e2e8f0;border-radius:8px;"" cellpadding=""0"" cellspacing=""0"">
                <tr>
                  <td style=""padding:12px 16px;font-size:13px;color:#475569;text-transform:uppercase;letter-spacing:1px;"">Topic</td>
                </tr>
                <tr>
                  <td style=""padding:12px 16px;border-top:1px solid #e2e8f0;"">
                    <div style=""font-size:15px;color:#0f172a;font-weight:600;"">{safeTopic}</div>
                    <div style=""font-size:13px;color:#475569;margin-top:4px;"">{safeDesc}</div>
                  </td>
                </tr>
              </table>
            </td>
          </tr>
          {noteBlock}
          <tr>
            <td style=""padding:24px 24px 0 24px;"">
              <a href=""{safeAction}"" style=""background:{brandHex};color:#ffffff;text-decoration:none;padding:12px 20px;border-radius:8px;display:inline-block;font-weight:600;"">Review invitation</a>
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
}
