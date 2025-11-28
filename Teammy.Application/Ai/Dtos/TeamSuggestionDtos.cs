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
    IReadOnlyList<string>? RequiredSkills,
    IReadOnlyList<string> MatchingSkills
);

public sealed record ProfilePostSuggestionDto(
    Guid PostId,
    Guid OwnerUserId,
    string OwnerDisplayName,
    string Title,
    string? Description,
    Guid? MajorId,
    DateTime CreatedAt,
    int Score,
    string? SkillsText,
    string? PrimaryRole,
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

public sealed record AutoAssignTopicResultDto(
    Guid GroupId,
    Guid TopicId,
    string TopicTitle,
    int Score
);

public sealed record AutoAssignTopicBatchResultDto(
    int AssignedCount,
    IReadOnlyList<AutoAssignTopicResultDto> Assignments,
    IReadOnlyList<Guid> SkippedGroupIds
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
