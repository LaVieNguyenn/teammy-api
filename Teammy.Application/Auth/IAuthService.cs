namespace Teammy.Application.Auth;

public sealed record AuthResult(string AccessToken, Guid UserId, string Email, string Name, string Role, string? PhotoUrl);

public interface IAuthService
{
    Task<AuthResult> LoginWithFirebaseAsync(string idToken, CancellationToken ct);
    Task<(Guid Id, string Email, string Name, string Role, string? PhotoUrl)> GetMeAsync(Guid userId, CancellationToken ct);
}
