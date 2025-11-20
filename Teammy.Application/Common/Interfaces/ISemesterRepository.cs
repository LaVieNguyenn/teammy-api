using Teammy.Application.Semesters.Dtos;

namespace Teammy.Application.Common.Interfaces;

public interface ISemesterRepository
{
    Task<Guid> CreateAsync(string season, int year, DateOnly startDate, DateOnly endDate, CancellationToken ct);
    Task<bool> UpdateAsync(Guid semesterId, string season, int year, DateOnly startDate, DateOnly endDate, CancellationToken ct);
    Task<bool> UpsertPolicyAsync(Guid semesterId, SemesterPolicyUpsertRequest req, CancellationToken ct);
    Task<bool> ActivateAsync(Guid semesterId, CancellationToken ct);
}
