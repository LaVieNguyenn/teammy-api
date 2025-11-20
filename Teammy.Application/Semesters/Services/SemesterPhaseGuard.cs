using Teammy.Application.Common.Interfaces;
using Teammy.Application.Semesters.Dtos;
using Teammy.Domain.Semesters;

namespace Teammy.Application.Semesters.Services;
public sealed class SemesterPhaseGuard
{
    private readonly ISemesterReadOnlyQueries _queries;

    public SemesterPhaseGuard(ISemesterReadOnlyQueries queries)
    {
        _queries = queries;
    }
    public async Task<SemesterDetailDto> GetSemesterAsync(
        Guid semesterId,
        CancellationToken ct)
    {
        var sem = await _queries.GetByIdAsync(semesterId, ct);
        if (sem is null)
            throw new InvalidOperationException("Semester not found");
        return sem;
    }

    public async Task EnsurePhaseAsync(
        Guid semesterId,
        string featureName,
        CancellationToken ct,
        params SemesterPhase[] allowed)
    {
        var sem = await GetSemesterAsync(semesterId, ct);

        if (allowed is null || allowed.Length == 0)
        {
            throw new InvalidOperationException(
                $"{featureName} no allowed phase configured");
        }

        if (!allowed.Contains(sem.Phase))
        {
            throw new InvalidOperationException(
                $"{featureName} not allowed in phase:{sem.Phase}");
        }
    }
}
