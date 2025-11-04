using Teammy.Application.Auth.Dtos;
using Teammy.Application.Common.Interfaces;

namespace Teammy.Application.Auth.Services;

/// <summary>
/// Xử lý use-case đăng nhập:
/// - Verify Firebase ID token (IExternalTokenVerifier)
/// - Tìm user đã được import & đang active (IUserRepository)
/// - Cấp JWT (ITokenService)
/// Không tạo user mới theo business rule.
/// </summary>
public sealed class AuthenticationService(
    IExternalTokenVerifier externalTokenVerifier,
    IUserRepository userRepository,
    ITokenService tokenService)
{
    public async Task<LoginResponse> LoginWithFirebaseAsync(LoginRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.IdToken))
            throw new ArgumentException("idToken is required.");

        var ext = await externalTokenVerifier.VerifyAsync(request.IdToken, ct);

        var user = await userRepository.FindActiveByEmailAsync(ext.Email, ct);
        if (user is null)
            throw new UnauthorizedAccessException("Account is not provisioned or inactive.");

        var jwt = tokenService.CreateAccessToken(
            user.Id, user.Email, user.DisplayName, user.RoleName, user.SkillsCompleted);

        return new LoginResponse(jwt, user.Id, user.Email, user.DisplayName, user.RoleName, user.SkillsCompleted);
    }
}
