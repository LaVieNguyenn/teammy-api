using Microsoft.EntityFrameworkCore;
using Teammy.Application.Common.Interfaces;
using Teammy.Infrastructure.Persistence;
using Teammy.Infrastructure.Persistence.Models;

namespace Teammy.Infrastructure.Persistence.Repositories;

public sealed class StudentSemesterWriteRepository(AppDbContext db) : IStudentSemesterWriteRepository
{
    public async Task SetCurrentSemesterAsync(Guid userId, Guid semesterId, CancellationToken ct)
    {
        var current = await db.student_semesters
            .FirstOrDefaultAsync(x => x.user_id == userId && x.is_current, ct);
        if (current is not null && current.semester_id == semesterId)
            return;
        if (current is not null)
            current.is_current = false;

        var existing = await db.student_semesters
            .FirstOrDefaultAsync(x => x.user_id == userId && x.semester_id == semesterId, ct);

        if (existing is null)
        {
            db.student_semesters.Add(new student_semester
            {
                user_id = userId,
                semester_id = semesterId,
                is_current = true,
                created_at = DateTime.UtcNow
            });
        }
        else
        {
            existing.is_current = true;
        }

        await db.SaveChangesAsync(ct);
    }
}
