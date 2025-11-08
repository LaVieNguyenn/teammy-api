namespace Teammy.Application.Posts.Dtos;

public sealed record PostSemesterDto(
    Guid   SemesterId,
    string? Season,
    int?   Year,
    DateOnly? StartDate,
    DateOnly? EndDate,
    bool   IsActive
);

public sealed record PostGroupDto(
    Guid   GroupId,
    string Name,
    string? Description,
    string Status,
    int    MaxMembers,
    Guid?  MajorId,
    Guid?  TopicId
);

public sealed record PostMajorDto(
    Guid   MajorId,
    string MajorName
);

public sealed record PostUserDto(
    Guid   UserId,
    string Email,
    string DisplayName,
    string? AvatarUrl,
    bool   EmailVerified
);

