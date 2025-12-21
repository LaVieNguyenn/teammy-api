namespace Teammy.Application.Announcements.Dtos;

public sealed record AnnouncementPlanningOverviewRequest(
    Guid MajorId
);

public sealed record AnnouncementPlanningOverviewDto(
    Guid SemesterId,
    string SemesterLabel,
    Guid MajorId,
    string? MajorName,
    int GroupsWithoutTopicCount,
    int GroupsWithoutMemberCount,
    int StudentsWithoutGroupCount,
    IReadOnlyList<PlanningGroupItemDto> GroupsWithoutTopic,
    IReadOnlyList<PlanningGroupItemDto> GroupsWithoutMember,
    IReadOnlyList<PlanningStudentItemDto> StudentsWithoutGroup
);

public sealed record PlanningGroupItemDto(
    Guid GroupId,
    string Name,
    string? Description,
    Guid? TopicId,
    Guid? MentorId,
    int MaxMembers,
    int CurrentMembers,
    string Status
);

public sealed record PlanningStudentItemDto(
    Guid StudentId,
    string DisplayName,
    Guid MajorId,
    string? PrimaryRole,
    IReadOnlyList<string> SkillTags
);
