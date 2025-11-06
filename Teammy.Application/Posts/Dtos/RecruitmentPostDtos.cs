namespace Teammy.Application.Posts.Dtos;

public sealed class CreateRecruitmentPostRequest
{
    public Guid GroupId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Skills { get; set; }
    public int? Limit { get; set; }
    public Guid? MajorId { get; set; }
}

public sealed record RecruitmentPostSummaryDto(
    Guid   Id,
    Guid   SemesterId,
    string Title,
    string Status,
    Guid?  GroupId,
    Guid?  MajorId,
    string? PositionNeeded,
    int    CurrentMembers,
    string? Description
);

public sealed record RecruitmentPostDetailDto(
    Guid   Id,
    Guid   SemesterId,
    string Title,
    string Status,
    Guid?  GroupId,
    Guid?  MajorId,
    string? Description,
    string? PositionNeeded,
    DateTime CreatedAt,
    int     CurrentMembers
);

public sealed record CreateApplicationRequest(string? Message);

public sealed record ApplicationDto(
    Guid   ApplicationId,
    Guid?  ApplicantUserId,
    Guid?  ApplicantGroupId,
    string Status,
    string? Message,
    DateTime CreatedAt,
    string? ApplicantEmail,
    string? ApplicantDisplayName
);

public sealed class UpdateRecruitmentPostRequest
{
    public string? Status { get; set; } // open | closed | full
    public string? Title { get; set; }
    public string? Description { get; set; }
    public string? Skills { get; set; }
}
