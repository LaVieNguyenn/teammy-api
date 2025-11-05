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
        string brandHex = "#F97316"
    )
    {
        var subject = $"{appName} • {leaderName} invited you to {groupName}";
        var safeLeader = System.Net.WebUtility.HtmlEncode(leaderName);
        var safeLeaderEmail = System.Net.WebUtility.HtmlEncode(leaderEmail);
        var safeGroup = System.Net.WebUtility.HtmlEncode(groupName);
        var safeAction = System.Net.WebUtility.HtmlEncode(actionUrl);
        var initial = string.IsNullOrWhiteSpace(logoUrl) ? (appName.Length > 0 ? appName.Substring(0,1).ToUpper() : "T") : "";

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
            <td style=""padding:24px 24px 8px 24px;"">
              <table width=""100%""><tr>
              
              </tr></table>
            </td>
          </tr>
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
}

