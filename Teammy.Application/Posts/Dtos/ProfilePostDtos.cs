namespace Teammy.Application.Posts.Dtos;

public sealed class CreateProfilePostRequest
{
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Skills { get; set; }
    public Guid? MajorId { get; set; }
}

public sealed record ProfilePostUserDto(
    Guid   UserId,
    string Email,
    string DisplayName,
    string? AvatarUrl,
    bool   EmailVerified,
    string? Phone,
    string? StudentCode,
    string? Gender,
    Guid?  MajorId,
    string? MajorName,
    string? Skills,
    bool   SkillsCompleted,
    bool   IsActive,
    DateTime CreatedAt,
    DateTime UpdatedAt
);

public sealed record ProfilePostSummaryDto(
    Guid   Id,
    Guid   SemesterId,
    string? SemesterName,
    PostSemesterDto? Semester,
    string Title,
    string Status,
    string Type,
    Guid?  UserId,
    string? UserDisplayName,
    ProfilePostUserDto? User,
    Guid?  MajorId,
    string? MajorName,
    PostMajorDto? Major,
    string? Description,
    string? Skills,
    DateTime CreatedAt
);

public sealed record ProfilePostDetailDto(
    Guid   Id,
    Guid   SemesterId,
    string? SemesterName,
    PostSemesterDto? Semester,
    string Title,
    string Status,
    string Type,
    Guid?  UserId,
    string? UserDisplayName,
    ProfilePostUserDto? User,
    Guid?  MajorId,
    string? MajorName,
    PostMajorDto? Major,
    string? Description,
    DateTime CreatedAt,
    string? Skills
);

public sealed record ProfilePostInvitationDto(
    Guid   CandidateId,
    Guid   PostId,
    Guid   GroupId,
    string GroupName,
    string Status,
    DateTime CreatedAt,
    Guid   SemesterId,
    Guid?  GroupMajorId,
    string? GroupMajorName,
    Guid?  LeaderUserId,
    string? LeaderDisplayName,
    string? LeaderEmail
);

public sealed record ProfilePostInvitationDetail(
    Guid CandidateId,
    Guid PostId,
    Guid GroupId,
    Guid SemesterId,
    Guid? GroupMajorId,
    string Status
);
