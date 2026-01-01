namespace Teammy.Application.ProjectTracking.Dtos;

public sealed record BacklogItemVm(
    Guid BacklogItemId,
    Guid GroupId,
    string Title,
    string? Description,
    string Status,
    string? Priority,
    string? Category,
    int? StoryPoints,
    Guid? OwnerUserId,
    string? OwnerDisplayName,
    DateTime? DueDate,
    Guid? LinkedTaskId,
    Guid? ColumnId,
    string? ColumnName,
    bool ColumnIsDone,
    Guid? MilestoneId,
    string? MilestoneName,
    DateTime CreatedAt,
    DateTime UpdatedAt
);

public sealed record CreateBacklogItemRequest(
    string Title,
    string? Description,
    string? Priority,
    string? Category,
    int? StoryPoints,
    DateTime? DueDate,
    Guid? OwnerUserId
);

public sealed record UpdateBacklogItemRequest(
    string Title,
    string? Description,
    string? Priority,
    string? Category,
    int? StoryPoints,
    DateTime? DueDate,
    string Status,
    Guid? OwnerUserId
);

public sealed record PromoteBacklogItemRequest(Guid ColumnId, string? TaskStatus, DateTime? TaskDueDate);

public sealed record MilestoneVm(
    Guid MilestoneId,
    Guid GroupId,
    string Name,
    string Status,
    DateOnly? TargetDate,
    DateTime? CompletedAt,
    string? Description,
    int TotalItems,
    int CompletedItems,
    decimal CompletionPercent,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    IReadOnlyList<MilestoneItemStatusVm>? Items
);

public sealed record CreateMilestoneRequest(string Name, string? Description, DateOnly? TargetDate);

public sealed record UpdateMilestoneRequest(string Name, string? Description, DateOnly? TargetDate, string Status, DateTime? CompletedAt);

public sealed record AssignMilestoneItemsRequest(IReadOnlyList<Guid> BacklogItemIds);

public sealed record ProjectReportVm(
    ProjectSummaryVm Project,
    TaskReportVm Tasks,
    TeamSnapshotVm Team
);

public sealed record MemberScoreQuery(
    DateOnly From,
    DateOnly To,
    int? High,
    int? Medium,
    int? Low
);

public sealed record MemberScoreReportVm(
    MemberScoreRangeVm Range,
    MemberScoreWeightsVm Weights,
    IReadOnlyList<MemberScoreVm> Members
);

public sealed record MemberScoreRangeVm(DateOnly From, DateOnly To);

public sealed record MemberScoreWeightsVm(int High, int Medium, int Low);

public sealed record MemberTaskCountsVm(int Assigned, int Done);

public sealed record MemberPriorityScoreVm(int Done, int Score);

public sealed record MemberTaskDetailVm(
    Guid TaskId,
    string Title,
    string Priority,
    int Weight,
    string Status,
    DateTime? CompletedAt,
    int ScoreContributed
);

public sealed record MemberScoreVm(
    Guid MemberId,
    string MemberName,
    int ScoreTotal,
    int DeliveryScore,
    int QualityScore,
    int CollabScore,
    MemberTaskCountsVm Tasks,
    IReadOnlyDictionary<string, MemberPriorityScoreVm> ByPriority,
    IReadOnlyList<MemberTaskDetailVm> TaskDetails
);

public sealed record ProjectSummaryVm(
    Guid GroupId,
    string GroupName,
    string Status,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    decimal CompletionPercent,
    int TotalItems,
    int ActiveItems,
    int CompletedItems,
    int BlockedItems,
    int OverdueItems,
    int DueSoonItems,
    int MilestoneCount,
    DateOnly? NextMilestoneTargetDate,
    string? NextMilestoneName,
    Guid? NextMilestoneId
);

public sealed record TeamSnapshotVm(
    int ActiveMemberCount,
    IReadOnlyList<MemberProfileVm> Leaders,
    MemberProfileVm? Mentor
);

public sealed record MemberProfileVm(Guid UserId, string DisplayName);

public sealed record TaskReportVm(
    BacklogStatusSummary Backlog,
    IReadOnlyList<ColumnProgressVm> Columns,
    IReadOnlyList<MilestoneProgressVm> Milestones
);

public sealed record BacklogStatusSummary(
    int Total,
    int Planned,
    int Ready,
    int InProgress,
    int Blocked,
    int Completed,
    int Archived,
    int NotStarted,
    int Active,
    int Remaining,
    int Overdue,
    int DueSoon,
    decimal CompletionPercent,
    decimal ActivePercent,
    decimal NotStartedPercent,
    decimal BlockedPercent,
    decimal ArchivedPercent
);

public sealed record ColumnProgressVm(Guid ColumnId, string ColumnName, bool IsDone, int TaskCount);

public sealed record MilestoneProgressVm(
    Guid MilestoneId,
    string Name,
    string Status,
    DateOnly? TargetDate,
    int TotalItems,
    int CompletedItems,
    decimal CompletionPercent,
    IReadOnlyList<MilestoneItemStatusVm>? Items
);

public sealed record MilestoneItemStatusVm(
    Guid BacklogItemId,
    string Title,
    string Status,
    DateTime? DueDate,
    Guid? LinkedTaskId,
    string? ColumnName,
    bool? ColumnIsDone
);

// Overdue Actions Panel DTOs
public sealed record MilestoneOverdueActionsVm(
    Guid MilestoneId,
    string MilestoneName,
    DateOnly? TargetDate,
    bool IsOverdue,
    int TotalItems,
    int CompletedItems,
    int IncompleteItems,
    int OverdueItems,
    int TasksDueAfterMilestone,
    IReadOnlyList<OverdueBacklogItemVm> OverdueBacklogItems,
    IReadOnlyList<TaskDueAfterMilestoneVm> TasksDueAfter
);

public sealed record OverdueBacklogItemVm(
    Guid BacklogItemId,
    string Title,
    DateTime? DueDate,
    string Status,
    Guid? LinkedTaskId,
    string? ColumnName,
    bool? ColumnIsDone
);

public sealed record TaskDueAfterMilestoneVm(
    Guid BacklogItemId,
    string Title,
    DateTime? DueDate,
    Guid? LinkedTaskId,
    string? ColumnName
);

public sealed record ExtendMilestoneRequest(DateOnly NewTargetDate);

public sealed record MoveMilestoneTasksRequest(
    Guid? TargetMilestoneId,  // Chỉ cần khi CreateNewMilestone = false
    bool CreateNewMilestone,
    string? NewMilestoneName,
    DateOnly? NewMilestoneTargetDate,
    string? NewMilestoneDescription
);

public sealed record MilestoneActionResultVm(
    bool Success,
    string Action,
    string Message,
    Guid? NewMilestoneId
);

// Timeline DTOs
public sealed record TimelineVm(
    DateOnly StartDate,
    DateOnly EndDate,
    IReadOnlyList<TimelineMilestoneVm> Milestones,
    IReadOnlyList<TimelineTaskVm> Tasks,
    IReadOnlyList<TimelineBacklogItemVm> BacklogItems
);

public sealed record TimelineMilestoneVm(
    Guid MilestoneId,
    string Name,
    string Status,
    DateOnly? TargetDate,
    DateTime? CompletedAt,
    string? Description,
    int TotalItems,
    int CompletedItems,
    decimal CompletionPercent,
    bool IsOverdue
);

public sealed record TimelineTaskVm(
    Guid TaskId,
    Guid? BacklogItemId,
    string Title,
    string? Priority,
    DateTime? DueDate,
    string? Status,
    Guid ColumnId,
    string ColumnName,
    bool ColumnIsDone,
    Guid? MilestoneId,
    string? MilestoneName,
    IReadOnlyList<Guid> AssigneeIds
);

public sealed record TimelineBacklogItemVm(
    Guid BacklogItemId,
    string Title,
    string Status,
    string? Priority,
    DateTime? DueDate,
    Guid? OwnerUserId,
    string? OwnerDisplayName,
    Guid? MilestoneId,
    string? MilestoneName,
    Guid? LinkedTaskId,
    bool IsOverdue
);
