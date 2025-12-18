using Teammy.Application.Posts.Dtos;
using Teammy.Application.Topics.Dtos;

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
)
{
    public RecruitmentPostDetailDto? Detail { get; init; }
    public string? AiReason { get; init; }
    public string? AiBalanceNote { get; init; }
}

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
)
{
    public ProfilePostDetailDto? Detail { get; init; }
    public string? AiReason { get; init; }
    public string? AiBalanceNote { get; init; }
}

public sealed record TopicSuggestionDto(
    Guid TopicId,
    string Title,
    string? Description,
    int Score,
    bool CanTakeMore,
    IReadOnlyList<string> MatchingSkills,
    IReadOnlyList<string> TopicSkills
)
{
    public TopicDetailDto? Detail { get; init; }
    public string? AiReason { get; init; }
    public string? AiBalanceNote { get; init; }
}

public sealed record AutoAssignTopicResultDto(
    Guid GroupId,
    Guid TopicId,
    string TopicTitle,
    int Score
);

public sealed record AutoAssignTopicBatchResultDto(
    int AssignedCount,
    IReadOnlyList<AutoAssignTopicResultDto> Assignments,
    IReadOnlyList<Guid> SkippedGroupIds,
    IReadOnlyList<TopicAssignmentIssueDto> Issues
);

public sealed record TopicAssignmentIssueDto(
    Guid GroupId,
    string Reason
);

public sealed record AutoAssignmentRecordDto(
    Guid StudentId,
    Guid GroupId,
    string GroupName,
    string SuggestedRole
);

public sealed record StudentAssignmentIssueDto(
    Guid StudentId,
    string Reason
);

public sealed record GroupAssignmentIssueDto(
    Guid GroupId,
    string Reason
);

public sealed record AutoAssignTeamsResultDto(
    int AssignedCount,
    IReadOnlyList<AutoAssignmentRecordDto> Assignments,
    IReadOnlyList<Guid> UnassignedStudentIds,
    IReadOnlyList<StudentAssignmentIssueDto> UnassignedStudents,
    IReadOnlyList<Guid> GroupsStillOpen,
    IReadOnlyList<GroupAssignmentIssueDto> GroupIssues,
    int NewGroupsCreated,
    IReadOnlyList<AutoResolveNewGroupDto> NewGroups
);
