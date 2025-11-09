using System.Text.Json.Serialization;

namespace Teammy.Application.Posts.Dtos;

public sealed class CreateRecruitmentPostRequest
{
    public Guid GroupId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Skills { get; set; }
    public int? Limit { get; set; }
    public Guid? MajorId { get; set; }

    // Aliases to accept alternative names from clients
    [JsonPropertyName("position_needed")]
    public string? PositionNeeded
    {
        get => Skills;
        set => Skills = value;
    }

    [JsonPropertyName("positionNeeded")]
    public string? PositionNeededCamel
    {
        get => Skills;
        set => Skills = value;
    }
}

public sealed record RecruitmentPostSummaryDto(
    Guid   Id,
    Guid   SemesterId,
    string? SemesterName,
    PostSemesterDto? Semester,
    string Title,
    string Status,
    string Type,
    Guid?  GroupId,
    string? GroupName,
    PostGroupDto? Group,
    Guid?  MajorId,
    string? MajorName,
    PostMajorDto? Major,
    string? PositionNeeded,
    int    CurrentMembers,
    string? Description,
    DateTime CreatedAt,
    DateTime? ApplicationDeadline
);

public sealed record RecruitmentPostDetailDto(
    Guid   Id,
    Guid   SemesterId,
    string? SemesterName,
    PostSemesterDto? Semester,
    string Title,
    string Status,
    string Type,
    Guid?  GroupId,
    string? GroupName,
    PostGroupDto? Group,
    Guid?  MajorId,
    string? MajorName,
    PostMajorDto? Major,
    string? Description,
    string? PositionNeeded,
    DateTime CreatedAt,
    int     CurrentMembers,
    DateTime? ApplicationDeadline
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

    // Aliases to accept alternative names from clients
    [JsonPropertyName("position_needed")]
    public string? PositionNeeded
    {
        get => Skills;
        set => Skills = value;
    }

    [JsonPropertyName("positionNeeded")]
    public string? PositionNeededCamel
    {
        get => Skills;
        set => Skills = value;
    }
}
