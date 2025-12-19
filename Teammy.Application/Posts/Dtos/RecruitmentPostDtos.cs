using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Teammy.Application.Posts.Dtos;

public sealed class CreateRecruitmentPostRequest
{
    public Guid GroupId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    [JsonPropertyName("required_skills")]
    public List<string>? RequiredSkills { get; set; }
    public Guid? MajorId { get; set; }
    public DateTime? ExpiresAt { get; set; }
    [JsonPropertyName("position_needed")]
    public string? PositionNeeded { get; set; }
}

public sealed record RecruitmentPostSummaryDto(
    Guid Id,
    Guid SemesterId,
    string? SemesterName,
    PostSemesterDto? Semester,
    string Title,
    string Status,
    string Type,
    Guid? GroupId,
    string? GroupName,
    PostGroupDto? Group,
    Guid? MajorId,
    string? MajorName,
    PostMajorDto? Major,
    string? PositionNeeded,
    [property: JsonPropertyName("required_skills")] IReadOnlyList<string>? RequiredSkills,
    int CurrentMembers,
    string? Description,
    DateTime CreatedAt,
    DateTime? ApplicationDeadline,
    bool HasApplied,
    Guid? MyApplicationId,
    string? MyApplicationStatus,
    int ApplicationsCount
);

public sealed record RecruitmentPostDetailDto(
    Guid Id,
    Guid SemesterId,
    string? SemesterName,
    PostSemesterDto? Semester,
    string Title,
    string Status,
    string Type,
    Guid? GroupId,
    string? GroupName,
    PostGroupDto? Group,
    Guid? MajorId,
    string? MajorName,
    PostMajorDto? Major,
    string? Description,
    string? PositionNeeded,
    [property: JsonPropertyName("required_skills")] IReadOnlyList<string>? RequiredSkills,
    DateTime CreatedAt,
    int CurrentMembers,
    DateTime? ApplicationDeadline,
    bool HasApplied,
    Guid? MyApplicationId,
    string? MyApplicationStatus,
    int ApplicationsCount
);

public sealed record CreateApplicationRequest(string? Message);

public sealed record ApplicationDto(
    Guid ApplicationId,
    Guid? ApplicantUserId,
    Guid? ApplicantGroupId,
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
    [JsonPropertyName("required_skills")]
    public List<string>? RequiredSkills { get; set; }

    [JsonPropertyName("position_needed")]
    public string? PositionNeeded { get; set; }
    [JsonPropertyName("expires_at")]
    public DateTime? ExpiresAt { get; set; }
}
