namespace Teammy.Application.Common.Interfaces;

public interface IExternalTokenVerifier
{
    Task<ExternalUserInfo> VerifyAsync(string idToken, CancellationToken ct);
}

public sealed record ExternalUserInfo(string Email, bool EmailVerified, string? DisplayName, string? PhotoUrl);
