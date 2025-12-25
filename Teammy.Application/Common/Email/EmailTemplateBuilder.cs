using System.Net;

namespace Teammy.Application.Common.Email;

public static class EmailTemplateBuilder
{
    public static string Build(
        string subject,
        string headline,
        string messageHtml,
        string buttonText,
        string buttonUrl,
        string? footerText = null)
    {
        var safeHeadline = WebUtility.HtmlEncode(headline);
        var safeButtonUrl = WebUtility.HtmlEncode(buttonUrl);
        var safeButtonText = WebUtility.HtmlEncode(buttonText);
        var safeFooter = WebUtility.HtmlEncode(footerText ?? $"Copyright {DateTime.UtcNow:yyyy} Teammy. All rights reserved.");

        return $@"<!doctype html>
<html>
  <head>
    <meta name=""viewport"" content=""width=device-width, initial-scale=1"" />
    <meta http-equiv=""Content-Type"" content=""text/html; charset=UTF-8"" />
    <title>{WebUtility.HtmlEncode(subject)}</title>
  </head>
  <body style=""margin:0;background:#f3f6fb;font-family:Segoe UI,Arial,Helvetica,sans-serif;color:#0f172a;"">
    <table role=""presentation"" border=""0"" cellpadding=""0"" cellspacing=""0"" width=""100%"">
      <tr><td align=""center"" style=""padding:28px;"">
        <table role=""presentation"" width=""640"" style=""max-width:640px;background:#ffffff;border-radius:16px;border:1px solid #e2e8f0;box-shadow:0 14px 30px rgba(15,23,42,0.08);"" cellpadding=""0"" cellspacing=""0"">
          <tr>
            <td style=""padding:24px 28px 0 28px;"">
              <div style=""font-size:26px;font-weight:800;color:#0f172a;letter-spacing:0.3px;text-shadow:0 1px 0 #ffffff,0 2px 0 #dbeafe,0 3px 0 #bfdbfe,0 5px 10px rgba(15,23,42,0.15);"">Teammy.</div>
            </td>
          </tr>
          <tr>
            <td style=""padding:8px 28px 12px 28px;font-size:18px;font-weight:700;color:#0f172a;"">{safeHeadline}</td>
          </tr>
          <tr>
            <td style=""padding:0 28px 4px 28px;font-size:14px;color:#475569;"">
              {messageHtml}
            </td>
          </tr>
          <tr>
            <td style=""padding:18px 28px 8px 28px;"">
              <a href=""{safeButtonUrl}"" style=""background:#7db7ff;color:#0b1f3a;text-decoration:none;padding:12px 20px;border-radius:10px;display:inline-block;font-weight:700;border:1px solid #6ea8ff;box-shadow:0 6px 14px rgba(125,183,255,0.45);"">{safeButtonText}</a>
            </td>
          </tr>
          <tr>
            <td style=""padding:12px 28px 24px 28px;"">
              <div style=""height:1px;background:#e2e8f0;margin-bottom:10px;""></div>
              <div style=""font-size:12px;color:#94a3b8;"">{safeFooter}</div>
            </td>
          </tr>
        </table>
      </td></tr>
    </table>
  </body>
</html>";
    }
}
