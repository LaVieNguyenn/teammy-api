namespace Teammy.Application.Groups.Dtos;

public sealed record GroupSummaryDto(
    Guid Id,
    SemesterDto Semester,
    string Name,
    string? Description,
    string Status,
    int MaxMembers,
    TopicDto? Topic,
    MajorDto? Major,
    MentorDto? Mentor,
    int CurrentMembers,
    IReadOnlyList<string>? Skills
);
public record MentorDto(
    Guid Id,
    string DisplayName,
    string? AvatarUrl,
    string Email
);
public sealed record SemesterDto(Guid SemesterId, string Season, int? Year, DateOnly? StartDate, DateOnly?EndDate, bool IsActive);

public sealed record TopicDto(Guid TopicId, string Title, string? Description);

public sealed record MajorDto(Guid MajorId, string MajorName);

public sealed record GroupDetailDto(
    Guid   Id,
    Guid   SemesterId,
    string Name,
    string? Description,
    string Status,
    int    MaxMembers,
    Guid?  TopicId,
    Guid?  MajorId,
    int    CurrentMembers,
    IReadOnlyList<string>? Skills,
    Guid[]? MentorIds
);

public sealed record InviteUserRequest(Guid UserId);
