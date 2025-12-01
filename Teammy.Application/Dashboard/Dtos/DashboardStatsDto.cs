namespace Teammy.Application.Dashboard.Dtos;

public sealed record DashboardStatsDto(
    int TotalUsers,
    int ActiveUsers,
    int TotalTopics,
    int OpenTopics,
    int TotalGroups,
    int RecruitingGroups,
    int ActiveGroups,
    int TotalPosts,
    int GroupPosts,
    int ProfilePosts);
