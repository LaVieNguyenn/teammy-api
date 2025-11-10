using Microsoft.EntityFrameworkCore;
using Teammy.Application.Common.Interfaces;
using Teammy.Application.Common.Utils;
using Teammy.Infrastructure.Persistence;
using Teammy.Infrastructure.Persistence.Models;

namespace Teammy.Infrastructure.Persistence.Repositories;

public sealed class SemesterWriteRepository(AppDbContext db) : ISemesterWriteRepository
{
    public async Task<Guid> EnsureByCodeAsync(string anySemesterText, CancellationToken ct)
    {
        var (season, year) = SemesterCode.Parse(anySemesterText);

        var exist = await db.semesters.AsNoTracking()
            .Where(s => s.season!.ToLower() == season.ToLower() && s.year == year)
            .Select(s => (Guid?)s.semester_id)
            .FirstOrDefaultAsync(ct);

        if (exist is not null) return exist.Value;

        var (start, end) = DefaultWindow(season, year);

        var e = new semester
        {
            semester_id = Guid.NewGuid(),
            season      = season,              
            year        = year,                
            start_date  = start,               
            end_date    = end,                 
            is_active   = false,              
        };

        db.semesters.Add(e);
        await db.SaveChangesAsync(ct);
        return e.semester_id;
    }
    private static (DateOnly start, DateOnly end) DefaultWindow(string season, int year)
        => season switch
        {
            "SPRING" => (new DateOnly(year, 1, 1),  new DateOnly(year, 4, 30)),
            "SUMMER" => (new DateOnly(year, 5, 1),  new DateOnly(year, 8, 31)),
            "FALL"   => (new DateOnly(year, 9, 1),  new DateOnly(year, 12, 31)),
            _        => throw new ArgumentOutOfRangeException(nameof(season), $"Unknown season: {season}")
        };
}
