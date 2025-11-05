using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Teammy.Application.Common.Interfaces;

namespace Teammy.Infrastructure.Email;

public sealed class SmtpEmailSender(IConfiguration cfg, ILogger<SmtpEmailSender> logger) : IEmailSender
{
    public async Task<bool> SendAsync(string toEmail, string subject, string htmlBody, CancellationToken ct, string? replyToEmail = null, string? fromDisplayName = null)
    {
        var host = cfg["Email:Smtp:Host"];
        var port = int.TryParse(cfg["Email:Smtp:Port"], out var p) ? p : 587;
        var user = cfg["Email:Smtp:User"];
        var pass = cfg["Email:Smtp:Password"];
        var from = cfg["Email:Smtp:From"] ?? user;
        var ssl  = bool.TryParse(cfg["Email:Smtp:EnableSsl"], out var b) ? b : true;
        if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(user) || string.IsNullOrWhiteSpace(pass))
        {
            logger.LogWarning("SMTP not configured. Skip sending to {ToEmail}", toEmail);
            return false; 
        }
        try
        {
            using var client = new SmtpClient(host, port)
            {
                Credentials = new NetworkCredential(user, pass),
                EnableSsl = ssl
            };
            MailAddress fromAddr = string.IsNullOrWhiteSpace(fromDisplayName)
                ? new MailAddress(from!)
                : new MailAddress(from!, fromDisplayName);

            using var msg = new MailMessage(fromAddr, new MailAddress(toEmail))
            {
                Subject = subject,
                Body = htmlBody,
                IsBodyHtml = true
            };
            if (!string.IsNullOrWhiteSpace(replyToEmail))
            {
                msg.ReplyToList.Add(new MailAddress(replyToEmail));
            }

            await client.SendMailAsync(msg);
            logger.LogInformation("Email sent to {ToEmail} with subject '{Subject}'", toEmail, subject);
            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to send email to {ToEmail} with subject '{Subject}'", toEmail, subject);
            return false;
        }
    }
}
