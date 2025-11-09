namespace Teammy.Application.Users.Dtos;

public sealed record UserSearchDto(
    Guid   UserId,
    string Email,
    string DisplayName,
    string? AvatarUrl,
    bool   EmailVerified,
    Guid?  MajorId,
    bool   HasGroupInSemester
);

