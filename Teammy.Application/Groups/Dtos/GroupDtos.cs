namespace Teammy.Application.Groups.Dtos;

public sealed record GroupSummaryDto(
    Guid   Id,
    Guid   SemesterId,
    string Name,
    string? Description,
    string Status,
    int    MaxMembers,
    Guid?  TopicId,
    Guid?  MajorId,
    int    CurrentMembers
);

public sealed record GroupDetailDto(
    Guid   Id,
    Guid   SemesterId,
    string Name,
    string? Description,
    string Status,
    int    MaxMembers,
    Guid?  TopicId,
    Guid?  MajorId,
    int    CurrentMembers
);

public sealed record InviteUserRequest(Guid UserId);
