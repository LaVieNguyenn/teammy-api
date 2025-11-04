namespace Teammy.Application.Auth.Dtos;

public sealed record CurrentUserDto(
    Guid   UserId,
    string Email,
    string DisplayName,
    string Role,
    string? AvatarUrl,
    bool   EmailVerified,
    bool   SkillsCompleted
);
