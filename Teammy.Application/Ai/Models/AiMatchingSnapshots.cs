namespace Teammy.Application.Ai.Models;

public sealed record StudentProfileSnapshot(
    Guid UserId,
    Guid MajorId,
    Guid SemesterId,
    string DisplayName,
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

public sealed record TopicAvailabilitySnapshot(
    Guid TopicId,
    Guid SemesterId,
    Guid? MajorId,
    string Title,
    string? Description,
    long UsedByGroups,
    bool CanTakeMore
);

public sealed record GroupMemberSkillSnapshot(
    Guid UserId,
    Guid GroupId,
    string? SkillsJson
);
