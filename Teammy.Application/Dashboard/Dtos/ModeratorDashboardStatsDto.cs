using System;

namespace Teammy.Application.Dashboard.Dtos;

public sealed record ModeratorDashboardStatsDto(
    int TotalGroups,
    int GroupsMissingTopic,
    int GroupsMissingMentor,
    int StudentsWithoutGroup,
    Guid? SemesterId,
    string? SemesterLabel);
