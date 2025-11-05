namespace Teammy.Application.Common.Interfaces;

public interface IEmailSender
{
    // Returns true if an email was attempted and successfully sent; false if skipped or failed.
    Task<bool> SendAsync(string toEmail, string subject, string htmlBody, CancellationToken ct, string? replyToEmail = null, string? fromDisplayName = null);
}
