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

public sealed record TopicMatchSnapshot(
    Guid GroupId,
    Guid TopicId,
    Guid SemesterId,
    Guid? MajorId,
    string Title,
    string? Description,
    int SimpleScore
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

public sealed record GroupRoleMixSnapshot(
    Guid GroupId,
    int FrontendCount,
    int BackendCount,
    int OtherCount
);
