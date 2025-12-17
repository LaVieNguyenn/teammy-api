using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Teammy.Application.Common.Interfaces;

namespace Teammy.Infrastructure.Email;

public sealed class HttpEmailSender : IEmailSender
{
    private readonly IConfiguration _cfg;
    private readonly ILogger<HttpEmailSender> _logger;
    private static readonly HttpClient _http = new HttpClient();

    public HttpEmailSender(IConfiguration cfg, ILogger<HttpEmailSender> logger)
    { _cfg = cfg; _logger = logger; }

    public async Task<bool> SendAsync(string toEmail, string subject, string htmlBody, CancellationToken ct, string? replyToEmail = null, string? fromDisplayName = null)
    {
        var provider = (_cfg["Email:Provider"] ?? _cfg["Email:Http:Provider"] ?? "").Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(provider)) provider = "sendgrid"; 

        var apiKey = _cfg["Email:Http:ApiKey"];
        var fromEmail = _cfg["Email:Http:From"] ?? _cfg["Email:Smtp:From"] ?? _cfg["Email:Smtp:User"];
        var fromName = fromDisplayName ?? _cfg["Email:Http:FromName"];

        if (string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(fromEmail))
        {
            _logger.LogWarning("HTTP email provider not configured. Skip sending to {ToEmail}", toEmail);
            return false;
        }

        // minimal sanitize
        toEmail = toEmail?.Trim() ?? string.Empty;
        replyToEmail = string.IsNullOrWhiteSpace(replyToEmail) ? null : replyToEmail!.Trim();
        if (string.IsNullOrWhiteSpace(toEmail) || !toEmail.Contains('@'))
        {
            _logger.LogWarning("Invalid recipient email: {ToEmail}", toEmail);
            return false;
        }

        try
        {
            if (provider == "sendgrid")
            {
                var endpoint = _cfg["Email:Http:SendGrid:Endpoint"] ?? "https://api.sendgrid.com/v3/mail/send";
                using var req = new HttpRequestMessage(HttpMethod.Post, endpoint);
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
                req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

                object fromObj = string.IsNullOrWhiteSpace(fromName) ? new { email = fromEmail } : new { email = fromEmail, name = fromName };
                var payload = new
                {
                    personalizations = new[] { new { to = new[] { new { email = toEmail } } } },
                    from = fromObj,
                    reply_to = replyToEmail is null ? null : new { email = replyToEmail },
                    subject,
                    content = new[] { new { type = "text/html", value = htmlBody } }
                };
                var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull });
                req.Content = new StringContent(json, Encoding.UTF8, "application/json");

                using var resp = await _http.SendAsync(req, ct);
                if (!resp.IsSuccessStatusCode)
                {
                    var body = await resp.Content.ReadAsStringAsync(ct);
                    _logger.LogError("HTTP email send failed (SendGrid) {Status}: {Body}", (int)resp.StatusCode, body);
                    return false;
                }
            }
            else if (provider == "resend")
            {
                var endpoint = _cfg["Email:Http:Resend:Endpoint"] ?? "https://api.resend.com/emails";
                using var req = new HttpRequestMessage(HttpMethod.Post, endpoint);
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
                req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

                var fromHeader = string.IsNullOrWhiteSpace(fromName) ? fromEmail : $"{fromName} <{fromEmail}>";
                var payload = new
                {
                    from = fromHeader,
                    to = toEmail,
                    subject,
                    html = htmlBody,
                    reply_to = replyToEmail
                };
                var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull });
                req.Content = new StringContent(json, Encoding.UTF8, "application/json");

                using var resp = await _http.SendAsync(req, ct);
                if (!resp.IsSuccessStatusCode)
                {
                    var body = await resp.Content.ReadAsStringAsync(ct);
                    _logger.LogError("HTTP email send failed (Resend) {Status}: {Body}", (int)resp.StatusCode, body);
                    return false;
                }
            }
            else
            {
                _logger.LogError("Unknown Email:Provider '{Provider}'. Supported: sendgrid, resend", provider);
                return false;
            }

            _logger.LogInformation("Email sent via {Provider} to {ToEmail} subject '{Subject}'", provider, toEmail, subject);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "HTTP email exception while sending to {ToEmail}", toEmail);
            return false;
        }
    }
}
