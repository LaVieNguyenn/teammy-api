namespace Teammy.Application.Ai.Models;

public sealed record StudentProfileSnapshot(
    Guid UserId,
    Guid MajorId,
    Guid SemesterId,
    string DisplayName,
    double? Gpa,
    Guid? DesiredPositionId,
    string? DesiredPositionName,
    string? PrimaryRole,
    string? SkillsJson,
    bool SkillsCompleted
);

public sealed record GroupCapacitySnapshot(
    Guid GroupId,
    Guid SemesterId,
    Guid? MajorId,
    string Name,
    string? Description,
    int MaxMembers,
    int CurrentMembers,
    int RemainingSlots
);

public sealed record GroupRoleMixSnapshot(
    Guid GroupId,
    int FrontendCount,
    int BackendCount,
    int OtherCount
);

public sealed record RecruitmentPostSnapshot(
    Guid PostId,
    Guid SemesterId,
    Guid? MajorId,
    string? MajorName,
    string Title,
    string? Description,
    Guid? GroupId,
    string? GroupName,
    string Status,
    string? PositionNeeded,
    string? RequiredSkills,
    DateTime CreatedAt,
    DateTime? ApplicationDeadline
);

public sealed record ProfilePostSnapshot(
    Guid PostId,
    Guid SemesterId,
    Guid? MajorId,
    string Title,
    string? Description,
    Guid OwnerUserId,
    string OwnerDisplayName,
    string? SkillsJson,
    string? SkillsText,
    string? PrimaryRole,
    DateTime CreatedAt,
    string? DesiredPositionName
);

public sealed record TopicAvailabilitySnapshot(
    Guid TopicId,
    Guid SemesterId,
    Guid? MajorId,
    string Title,
    string? Description,
    string? SkillsJson,
    IReadOnlyList<string> SkillNames,
    long UsedByGroups,
    bool CanTakeMore
);

public sealed record GroupMemberSkillSnapshot(
    Guid UserId,
    Guid GroupId,
    string? SkillsJson
);

public sealed record GroupOverviewSnapshot(
    Guid GroupId,
    Guid SemesterId,
    Guid? MajorId,
    string? MajorName,
    string Name,
    string? Description,
    int MaxMembers,
    int CurrentMembers,
    int RemainingSlots,
    Guid? TopicId,
    Guid? MentorId,
    string Status
);
