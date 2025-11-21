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
    Guid   SemesterId,
    Guid?  MentorId,
    string Name,
    string? Description,
    string Status,
    int    MaxMembers,
    Guid?  MajorId,
    Guid?  TopicId,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    PostMajorDto? Major,
    PostTopicDto? Topic,
    PostUserDto? Mentor
);

public sealed record PostMajorDto(
    Guid   MajorId,
    string MajorName
);

public sealed record PostTopicDto(
    Guid   TopicId,
    Guid   SemesterId,
    Guid?  MajorId,
    string Title,
    string? Description,
    string Status,
    Guid   CreatedBy,
    DateTime CreatedAt
);
public sealed record PostTopicDtoDetail(
    Guid   TopicId,
    string Title,
    string? Description,
    string Status,
    Guid CreatedBy,
    DateTime? CreatedAt
);
public sealed record PostUserDto(
    Guid   UserId,
    string Email,
    string DisplayName,
    string? AvatarUrl,
    bool   EmailVerified
);
