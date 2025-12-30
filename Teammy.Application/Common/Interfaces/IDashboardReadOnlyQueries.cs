using System;
using Teammy.Application.Dashboard.Dtos;

namespace Teammy.Application.Common.Interfaces;

public interface IDashboardReadOnlyQueries
{
    Task<DashboardStatsDto> GetStatsAsync(Guid? semesterId, CancellationToken ct);
    Task<ModeratorDashboardStatsDto> GetModeratorStatsAsync(Guid? semesterId, CancellationToken ct);
}
