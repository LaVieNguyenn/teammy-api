namespace Teammy.Application.Groups.Dtos;

public sealed record GroupMemberDto(
    Guid   UserId,
    string Email,
    string DisplayName,
    string Role,        // leader | member
    DateTime JoinedAt,
    string? AvatarUrl
);

