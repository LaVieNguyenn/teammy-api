namespace Teammy.Application.Common.Interfaces;

public interface ITokenService
{
    string CreateAccessToken(Guid userId, string email, string displayName, string role, TokenSemesterInfo? semester);
}

public sealed record TokenSemesterInfo(
    Guid SemesterId,
    string Season,
    int Year,
    DateOnly StartDate,
    DateOnly EndDate
);
