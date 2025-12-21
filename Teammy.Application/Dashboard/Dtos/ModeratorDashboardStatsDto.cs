using System;

namespace Teammy.Application.Dashboard.Dtos;

public sealed record ModeratorDashboardStatsDto(
    int TotalGroups,
    int GroupsWithoutTopic,
    int GroupsWithoutMember,
    int StudentsWithoutGroup,
    Guid? SemesterId,
    string? SemesterLabel);
