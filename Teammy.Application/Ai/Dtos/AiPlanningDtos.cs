namespace Teammy.Application.Ai.Dtos;

public enum AiOptionSection
{
    All,
    GroupsWithoutTopic,
    GroupsNeedingMembers,
    StudentsWithoutGroup
}

public sealed record AiSummaryDto(
    Guid SemesterId,
    string SemesterName,
    DateTime GeneratedAt,
    int GroupsWithoutTopic,
    int GroupsUnderCapacity,
    int StudentsWithoutGroup
);

public sealed record PaginatedCollection<T>(
    int TotalCount,
    int Page,
    int PageSize,
    IReadOnlyList<T> Items
);

public sealed record AiOptionListDto(
    Guid SemesterId,
    string SemesterName,
    AiOptionSection Section,
    PaginatedCollection<GroupTopicOptionDto>? GroupsWithoutTopic,
    PaginatedCollection<GroupStaffingOptionDto>? GroupsNeedingMembers,
    PaginatedCollection<StudentPlacementOptionDto>? StudentsWithoutGroup,
    IReadOnlyList<NewGroupCreationOptionDto>? NewGroupPlans
);

public sealed record NewGroupCreationOptionDto(
    Guid MajorId,
    string? MajorName,
    int PolicyMinSize,
    int PolicyMaxSize,
    IReadOnlyList<PlannedNewGroupDto> Groups,
    IReadOnlyList<StudentAssignmentIssueDto> UnresolvedStudents
);

public sealed record PlannedNewGroupDto(
    string Key,
    Guid MajorId,
    string? MajorName,
    int MemberCount,
    IReadOnlyList<PlannedNewGroupStudentDto> Students,
    string Reason
);

public sealed record PlannedNewGroupStudentDto(
    Guid StudentId,
    string DisplayName,
    Guid MajorId,
    string? MajorName,
    string? PrimaryRole,
    IReadOnlyList<string> SkillTags
);

public sealed record GroupTopicOptionDto(
    Guid GroupId,
    string Name,
    string? Description,
    Guid? MajorId,
    string? MajorName,
    int MaxMembers,
    int CurrentMembers,
    int RemainingSlots,
    int Score,
    IReadOnlyList<TopicSuggestionDetailDto> Suggestions
);

public sealed record TopicSuggestionDetailDto(
    Guid TopicId,
    string Title,
    string? Description,
    int Score,
    IReadOnlyList<string>? MatchingSkills,
    string Reason
);

public sealed record GroupStaffingOptionDto(
    Guid GroupId,
    string Name,
    string? Description,
    Guid? MajorId,
    string? MajorName,
    int MaxMembers,
    int CurrentMembers,
    int RemainingSlots,
    int Score,
    IReadOnlyList<GroupCandidateSuggestionDto> SuggestedMembers
);

public sealed record GroupCandidateSuggestionDto(
    Guid StudentId,
    string DisplayName,
    Guid MajorId,
    string? MajorName,
    double? Gpa,
    string? DesiredPositionName,
    string? PrimaryRole,
    IReadOnlyList<string> SkillTags,
    int Score,
    string? Reason
);

public sealed record StudentPlacementOptionDto(
    Guid StudentId,
    string DisplayName,
    Guid MajorId,
    string? MajorName,
    double? Gpa,
    string? DesiredPositionName,
    string? PrimaryRole,
    IReadOnlyList<string> SkillTags,
    int Score,
    string? Reason,
    GroupPlacementSuggestionDto? SuggestedGroup,
    bool NeedsNewGroup
);

public sealed record GroupPlacementSuggestionDto(
    Guid GroupId,
    string Name,
    Guid? MajorId,
    string? MajorName,
    int Score,
    string? Reason
);

public sealed record AiAutoResolveResultDto(
    Guid SemesterId,
    string SemesterName,
    int StudentsAssigned,
    int TopicsAssigned,
    int NewGroupsCreated,
    IReadOnlyList<AutoAssignmentRecordDto> StudentAssignments,
    IReadOnlyList<AutoAssignTopicResultDto> TopicAssignments,
    IReadOnlyList<Guid> TopicSkippedGroupIds,
    IReadOnlyList<GroupAssignmentIssueDto> GroupAssignmentIssues,
    IReadOnlyList<AutoResolveNewGroupDto> NewGroups,
    IReadOnlyList<Guid> UnresolvedStudentIds,
    IReadOnlyList<StudentAssignmentIssueDto> UnresolvedStudents
);

public sealed record AutoResolveNewGroupDto(
    Guid GroupId,
    string Name,
    Guid? MajorId,
    string? MajorName,
    int MemberCount,
    Guid? TopicId,
    string? TopicTitle,
    IReadOnlyList<Guid> StudentIds
);
