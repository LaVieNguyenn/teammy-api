using Teammy.Domain.Semesters;

namespace Teammy.Application.Semesters.Dtos;

public sealed record SemesterSummaryDto(
    Guid SemesterId,
    string Season,
    int Year,
    DateOnly StartDate,
    DateOnly EndDate,
    bool IsActive,
    SemesterPhase Phase,
    SemesterPolicyDto? Policy
);
