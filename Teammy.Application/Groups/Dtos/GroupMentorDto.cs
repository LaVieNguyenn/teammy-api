namespace Teammy.Application.Groups.Dtos;

public sealed record GroupMentorDto(
    Guid   UserId,
    string Email,
    string DisplayName,
    string? AvatarUrl
);
