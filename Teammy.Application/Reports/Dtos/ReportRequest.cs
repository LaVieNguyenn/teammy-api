namespace Teammy.Application.Reports.Dtos;

public sealed class ReportRequest
{
    public Guid? SemesterId { get; init; }
    public Guid? GroupId { get; init; }
    public string? GroupStatus { get; init; }
    public Guid? MajorId { get; init; }
    public string? RecruitmentStatus { get; init; }

    public bool IncludeGroups { get; init; } = true;
    public bool IncludeGroupMembers { get; init; } = true;
    public bool IncludeRecruitmentPosts { get; init; } = true;
    public bool IncludeMilestones { get; init; }
    public bool IncludeActivityLogs { get; init; }
    public DateTime? StartDateUtc { get; init; }
    public DateTime? EndDateUtc { get; init; }

    private int _activityLogLimit = 200;
    public int ActivityLogLimit
    {
        get => _activityLogLimit;
        init => _activityLogLimit = value <= 0 ? 200 : value;
    }
}
