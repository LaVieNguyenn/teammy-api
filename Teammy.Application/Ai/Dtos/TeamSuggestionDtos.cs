namespace Teammy.Application.Ai.Dtos;

public sealed record RecruitmentPostSuggestionDto(
    Guid PostId,
    string Title,
    string? Description,
    Guid? GroupId,
    string? GroupName,
    Guid? MajorId,
    string? MajorName,
    DateTime CreatedAt,
    DateTime? ApplicationDeadline,
    int Score,
    string? PositionNeeded,
    string? RequiredSkills,
    IReadOnlyList<string> MatchingSkills
);

public sealed record TopicSuggestionDto(
    Guid TopicId,
    string Title,
    string? Description,
    int Score,
    bool CanTakeMore,
    IReadOnlyList<string> MatchingSkills
);

public sealed record AutoAssignmentRecordDto(
    Guid StudentId,
    Guid GroupId,
    string GroupName,
    string SuggestedRole
);

public sealed record AutoAssignTeamsResultDto(
    int AssignedCount,
    IReadOnlyList<AutoAssignmentRecordDto> Assignments,
    IReadOnlyList<Guid> UnassignedStudentIds,
    IReadOnlyList<Guid> GroupsStillOpen
);
