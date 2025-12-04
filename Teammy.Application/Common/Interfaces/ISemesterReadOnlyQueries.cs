using Teammy.Application.Semesters.Dtos;

namespace Teammy.Application.Common.Interfaces;

public interface ISemesterReadOnlyQueries
{
    Task<IReadOnlyList<SemesterSummaryDto>> ListAsync(CancellationToken ct);
    Task<SemesterDetailDto?> GetByIdAsync(Guid semesterId, CancellationToken ct);
    Task<SemesterDetailDto?> GetActiveAsync(CancellationToken ct);
    Task<SemesterPolicyDto?> GetPolicyAsync(Guid semesterId, CancellationToken ct);
    Task<bool> ExistsAsync(string normalizedSeason, int year, Guid? excludeSemesterId, CancellationToken ct);
    Task<int> CountByYearAsync(int year, CancellationToken ct);
}
