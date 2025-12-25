using Teammy.Application.Auth.Dtos;
using Teammy.Application.Common.Interfaces;

namespace Teammy.Application.Auth.Services;
public sealed class AuthenticationService(
    IExternalTokenVerifier externalTokenVerifier,
    IUserRepository userRepository,
    ITokenService tokenService,
    IStudentSemesterReadOnlyQueries studentSemesters,
    ISemesterReadOnlyQueries semesterQueries)
{
    public async Task<LoginResponse> LoginWithFirebaseAsync(LoginRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.IdToken))
            throw new ArgumentException("idToken is required.");

        var ext = await externalTokenVerifier.VerifyAsync(request.IdToken, ct);

        var user = await userRepository.FindActiveByEmailAsync(ext.Email, ct);
        if (user is null)
            throw new UnauthorizedAccessException("Account is not provisioned or inactive.");

        if (String.IsNullOrEmpty(user.AvatarUrl))
        {
            await userRepository.UpdateAsync(new Domain.Users.User(
                user.Id,
                user.Email,
                user.DisplayName,
                ext.PhotoUrl,
                user.EmailVerified,
                user.SkillsCompleted,
                user.IsActive,
                user.RoleName), ct);
            
        }
        TokenSemesterInfo? semesterInfo = null;
        if (string.Equals(user.RoleName, "student", StringComparison.OrdinalIgnoreCase))
        {
            var semesterId = await studentSemesters.GetCurrentSemesterIdAsync(user.Id, ct);
            if (semesterId.HasValue)
            {
                var semester = await semesterQueries.GetByIdAsync(semesterId.Value, ct);
                if (semester is not null)
                {
                    semesterInfo = new TokenSemesterInfo(
                        semester.SemesterId,
                        semester.Season,
                        semester.Year,
                        semester.StartDate,
                        semester.EndDate);
                }
            }
        }

        var jwt = tokenService.CreateAccessToken(
            user.Id, user.Email, user.DisplayName, user.RoleName, semesterInfo);

        return new LoginResponse(jwt, user.Id, user.Email, user.DisplayName, user.RoleName);
    }
}
