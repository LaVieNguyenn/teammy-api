using Teammy.Application.Common.Interfaces.Auth;
using Teammy.Application.Common.Interfaces.Persistence;

namespace Teammy.Application.Auth;

/// <summary>
/// Đăng nhập dựa trên email duy nhất được trường cung cấp.
/// </summary>
public sealed class AuthService : IAuthService
{
    private readonly IExternalTokenVerifier _verifier; // Verify Firebase ID Token
    private readonly ITokenService _tokens;            // Issue JWT
    private readonly IUserRepository _users;           // Repo trừu tượng

    public AuthService(IExternalTokenVerifier verifier, ITokenService tokens, IUserRepository users)
    {
        _verifier = verifier; _tokens = tokens; _users = users;
    }

    public async Task<AuthResult> LoginWithFirebaseAsync(string idToken, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(idToken))
            throw new ArgumentException("idToken required");

        var info = await _verifier.VerifyAsync(idToken, ct);

        // BẮT BUỘC email phải có và đã verify để tránh giả mạo email
        if (string.IsNullOrWhiteSpace(info.Email))
            throw new UnauthorizedAccessException("EMAIL_MISSING");
        if (!info.EmailVerified)
            throw new UnauthorizedAccessException("EMAIL_NOT_VERIFIED");

        var user = await _users.FindByEmailAsync(info.Email, includeRole: true, ct);
        if (user is null)
            throw new UnauthorizedAccessException("USER_NOT_IMPORTED");
        if (!user.IsActive)
            throw new InvalidOperationException("USER_INACTIVE");


        var jwt = _tokens.CreateAccessToken(user.Id, user.Email, user.DisplayName, user.RoleName, user.PhotoUrl);
        return new(jwt, user.Id, user.Email, user.DisplayName, user.RoleName, user.PhotoUrl);
    }

    public async Task<(Guid Id, string Email, string Name, string Role, string? PhotoUrl)> GetMeAsync(Guid userId, CancellationToken ct)
    {
        var u = await _users.FindByIdAsync(userId, includeRole: true, ct)
                 ?? throw new KeyNotFoundException("USER_NOT_FOUND");
        return (u.Id, u.Email, u.DisplayName, u.RoleName, u.PhotoUrl);
    }
}
