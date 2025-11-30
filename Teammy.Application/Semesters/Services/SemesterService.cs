using Teammy.Application.Common.Interfaces;
using Teammy.Application.Semesters.Dtos;
namespace Teammy.Application.Semesters.Services;

public sealed class SemesterService(
    ISemesterRepository repo,
    ISemesterReadOnlyQueries queries)
{
    public async Task<Guid> CreateAsync(SemesterUpsertRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Season))
            throw new ArgumentException("Season is required");

        if (req.StartDate > req.EndDate)
            throw new ArgumentException("StartDate must be <= EndDate");

        return await repo.CreateAsync(req.Season, req.Year, req.StartDate, req.EndDate, ct);
    }

    public async Task UpdateAsync(Guid id, SemesterUpsertRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Season))
            throw new ArgumentException("Season is required");

        if (req.StartDate > req.EndDate)
            throw new ArgumentException("StartDate must be <= EndDate");

        var ok = await repo.UpdateAsync(id, req.Season, req.Year, req.StartDate, req.EndDate, ct);
        if (!ok) throw new KeyNotFoundException("Semester not found");
    }

    public async Task<IReadOnlyList<SemesterSummaryDto>> GetSemestersAsync(CancellationToken ct)
    {
        await repo.EnsureCurrentStateAsync(ct);
        return await queries.ListAsync(ct);
    }

    public Task<SemesterDetailDto?> GetSemesterAsync(Guid id, CancellationToken ct)
        => queries.GetByIdAsync(id, ct);

    public async Task<SemesterDetailDto?> GetActiveSemesterAsync(CancellationToken ct)
    {
        await repo.EnsureCurrentStateAsync(ct);
        return await queries.GetActiveAsync(ct);
    }

    public Task<SemesterPolicyDto?> GetPolicyAsync(Guid id, CancellationToken ct)
        => queries.GetPolicyAsync(id, ct);

    public async Task UpsertPolicyAsync(Guid id, SemesterPolicyUpsertRequest req, CancellationToken ct)
    {
        if (req.TeamSelfSelectStart > req.TeamSelfSelectEnd)
            throw new ArgumentException("Invalid team self selection period");
        if (req.TopicSelfSelectStart > req.TopicSelfSelectEnd)
            throw new ArgumentException("Invalid topic self selection period");
        if (req.DesiredGroupSizeMin <= 0 || req.DesiredGroupSizeMax < req.DesiredGroupSizeMin)
            throw new ArgumentException("Invalid group size range");

        var ok = await repo.UpsertPolicyAsync(id, req, ct);
        if (!ok) throw new KeyNotFoundException("Semester not found");
    }

    public async Task ActivateAsync(Guid id, CancellationToken ct)
    {
        var ok = await repo.ActivateAsync(id, ct);
        if (!ok) throw new KeyNotFoundException("Semester not found");
    }
}
