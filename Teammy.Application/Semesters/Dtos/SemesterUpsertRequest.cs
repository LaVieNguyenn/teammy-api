namespace Teammy.Application.Semesters.Dtos;

public sealed record SemesterUpsertRequest(
    string Season,
    int Year,
    DateOnly StartDate,
    DateOnly EndDate
);
