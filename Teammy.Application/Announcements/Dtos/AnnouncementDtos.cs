namespace Teammy.Application.Announcements.Dtos;

public static class AnnouncementScopes
{
    public const string Global = "global";
    public const string Semester = "semester";
    public const string Role = "role";
    public const string Group = "group";
    public const string GroupsWithoutTopic = "groups_without_topic";
    public const string GroupsUnderstaffed = "groups_understaffed";
    public const string StudentsWithoutGroup = "students_without_group";

    public static readonly string[] All =
    {
        Global,
        Semester,
        Role,
        Group,
        GroupsWithoutTopic,
        GroupsUnderstaffed,
        StudentsWithoutGroup
    };

    public static bool IsValid(string? scope)
        => !string.IsNullOrWhiteSpace(scope)
           && All.Contains(scope.Trim().ToLowerInvariant());
}

public static class AnnouncementRoles
{
    public static readonly string[] Allowed =
    {
        "student", "leader", "mentor", "moderator", "admin"
    };

    public static bool IsValid(string? role)
        => !string.IsNullOrWhiteSpace(role)
           && Allowed.Contains(role.Trim().ToLowerInvariant());
}

public sealed record CreateAnnouncementRequest(
    Guid? SemesterId,
    string Scope,
    string Title,
    string Content,
    string? TargetRole,
    Guid? TargetGroupId,
    DateTime? PublishAt,
    DateTime? ExpireAt,
    bool Pinned
);

public sealed record AnnouncementFilter(
    bool IncludeExpired,
    bool PinnedOnly
);

public sealed record AnnouncementDto(
    Guid Id,
    Guid? SemesterId,
    string Scope,
    string? TargetRole,
    Guid? TargetGroupId,
    string Title,
    string Content,
    bool Pinned,
    DateTime PublishAt,
    DateTime? ExpireAt,
    Guid CreatedBy,
    string CreatedByName
);

public sealed record AnnouncementRecipient(Guid UserId, string Email, string? DisplayName);

public sealed record CreateAnnouncementCommand(
    Guid CreatedBy,
    Guid? SemesterId,
    string Scope,
    string Title,
    string Content,
    string? TargetRole,
    Guid? TargetGroupId,
    DateTime PublishAt,
    DateTime? ExpireAt,
    bool Pinned
);
