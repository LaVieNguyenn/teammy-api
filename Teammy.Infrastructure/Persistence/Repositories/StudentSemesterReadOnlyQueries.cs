using Microsoft.EntityFrameworkCore;
using Teammy.Application.Common.Interfaces;
using Teammy.Infrastructure.Persistence;

namespace Teammy.Infrastructure.Persistence.Repositories;

public sealed class StudentSemesterReadOnlyQueries(AppDbContext db) : IStudentSemesterReadOnlyQueries
{
    public Task<Guid?> GetCurrentSemesterIdAsync(Guid userId, CancellationToken ct)
        => db.student_semesters.AsNoTracking()
            .Where(x => x.user_id == userId && x.is_current)
            .Select(x => (Guid?)x.semester_id)
            .FirstOrDefaultAsync(ct);
}
