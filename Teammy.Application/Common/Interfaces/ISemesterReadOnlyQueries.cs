using Teammy.Application.Semesters.Dtos;

namespace Teammy.Application.Common.Interfaces;

public interface ISemesterReadOnlyQueries
{
    Task<IReadOnlyList<SemesterSummaryDto>> ListAsync(CancellationToken ct);
    Task<SemesterDetailDto?> GetByIdAsync(Guid semesterId, CancellationToken ct);
    Task<SemesterDetailDto?> GetActiveAsync(CancellationToken ct);
}
