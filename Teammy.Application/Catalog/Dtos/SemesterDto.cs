namespace Teammy.Application.Catalog.Dtos;

public sealed record SemesterDto(
    Guid      SemesterId,
    string?   Season,
    int?      Year,
    DateOnly? StartDate,
    DateOnly? EndDate,
    bool      IsActive
);

