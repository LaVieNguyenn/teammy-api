namespace Teammy.Application.Ai.Dtos;

public sealed record TeamSuggestionDto(
    Guid GroupId,
    string Name,
    string? Description,
    int Score,
    int RemainingSlots,
    bool NeedsFrontend,
    bool NeedsBackend
);

public sealed record TopicSuggestionDto(
    Guid TopicId,
    string Title,
    string? Description,
    int Score,
    bool CanTakeMore
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
