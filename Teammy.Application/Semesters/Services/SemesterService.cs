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
            throw new ArgumentException("StartDate must be < EndDate");
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        if (req.EndDate < today)
            throw new InvalidOperationException("Semester dates cannot be created in the past.");
        if (req.Year < today.Year)
            throw new InvalidOperationException("Semester year cannot be in the past.");
        ValidateSemesterYear(req.Year, req.StartDate, req.EndDate);

        var season = NormalizeSeason(req.Season);
        await EnsureSemesterConstraintsAsync(season, req.Year, null, ct);

        return await repo.CreateAsync(season, req.Year, req.StartDate, req.EndDate, ct);
    }

    public async Task UpdateAsync(Guid id, SemesterUpsertRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Season))
            throw new ArgumentException("Season is required");

        if (req.StartDate > req.EndDate)
            throw new ArgumentException("StartDate must be <= EndDate");
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        if (req.EndDate < today)
            throw new InvalidOperationException("Semester dates cannot be created in the past.");
        if (req.Year < today.Year)
            throw new InvalidOperationException("Semester year cannot be in the past.");
        ValidateSemesterYear(req.Year, req.StartDate, req.EndDate);

        var season = NormalizeSeason(req.Season);
        await EnsureSemesterConstraintsAsync(season, req.Year, id, ct);

        var ok = await repo.UpdateAsync(id, season, req.Year, req.StartDate, req.EndDate, ct);
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

        var semester = await queries.GetByIdAsync(id, ct);
        if (semester is null)
            throw new KeyNotFoundException("Semester not found");

        EnsurePolicyDatesInRange(req, semester);

        var ok = await repo.UpsertPolicyAsync(id, req, ct);
        if (!ok) throw new KeyNotFoundException("Semester not found");
    }

    public async Task ActivateAsync(Guid id, CancellationToken ct)
    {
        var policy = await queries.GetPolicyAsync(id, ct);
        if (policy is null)
            throw new InvalidOperationException("Semester policy must be create before activation.");

        var ok = await repo.ActivateAsync(id, ct);
        if (!ok) throw new KeyNotFoundException("Semester not found");
    }

    private async Task EnsureSemesterConstraintsAsync(string season, int year, Guid? excludeId, CancellationToken ct)
    {
        if (await queries.ExistsAsync(season, year, excludeId, ct))
            throw new InvalidOperationException("A semester with the same season and year already exists.");

        var total = await queries.CountByYearAsync(year, ct);
        if (excludeId.HasValue)
        {
            var existing = await queries.GetByIdAsync(excludeId.Value, ct);
            if (existing != null && existing.Year == year)
                total--; 
        }

        if (total >= 3)
            throw new InvalidOperationException("Each year can only have up to 3 semesters.");
    }

    private static string NormalizeSeason(string season)
        => season.Trim().ToUpperInvariant();

    private static void ValidateSemesterYear(int year, DateOnly start, DateOnly end)
    {
        if (start.Year != year || end.Year != year)
            throw new ArgumentException("StartDate and EndDate must available with year.");
    }

    private static void EnsurePolicyDatesInRange(SemesterPolicyUpsertRequest req, SemesterDetailDto semester)
    {
        void Check(DateOnly date, string fieldName)
        {
            if (date.Year != semester.Year)
                throw new ArgumentException($"{fieldName} must be semester year.");

            if (date >= semester.StartDate)
                throw new ArgumentException($"{fieldName} must be before semester start date.");
        }

        Check(req.TeamSelfSelectStart, nameof(req.TeamSelfSelectStart));
        Check(req.TeamSelfSelectEnd, nameof(req.TeamSelfSelectEnd));
        Check(req.TeamSuggestStart, nameof(req.TeamSuggestStart));
        Check(req.TopicSelfSelectStart, nameof(req.TopicSelfSelectStart));
        Check(req.TopicSelfSelectEnd, nameof(req.TopicSelfSelectEnd));
        Check(req.TopicSuggestStart, nameof(req.TopicSuggestStart));
    }
}
