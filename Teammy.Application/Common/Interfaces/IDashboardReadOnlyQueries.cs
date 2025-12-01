using Teammy.Application.Dashboard.Dtos;

namespace Teammy.Application.Common.Interfaces;

public interface IDashboardReadOnlyQueries
{
    Task<DashboardStatsDto> GetStatsAsync(CancellationToken ct);
}
