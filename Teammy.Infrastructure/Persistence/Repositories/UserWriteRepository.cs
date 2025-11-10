using Microsoft.EntityFrameworkCore;
using Teammy.Application.Common.Interfaces;
using Teammy.Infrastructure.Persistence;
using Teammy.Infrastructure.Persistence.Models;

namespace Teammy.Infrastructure.Persistence.Repositories;

public sealed class UserWriteRepository(AppDbContext db) : IUserWriteRepository
{
    public Task<bool> EmailExistsAnyAsync(string email, CancellationToken ct)
        => db.users.AsNoTracking().AnyAsync(u => u.email.ToLower() == email.ToLower(), ct);

    public async Task<Guid> CreateUserAsync(string email, string displayName,
                                            string? studentCode, string? gender,
                                            Guid? majorId,
                                            CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var entity = new user
        {
            user_id       = Guid.NewGuid(),
            email         = email,
            display_name  = displayName,
            student_code  = studentCode,
            gender        = string.IsNullOrWhiteSpace(gender) ? null : gender,
            major_id      = majorId,
            is_active     = true,       
            email_verified= true,      
            created_at    = now,
            updated_at    = now
        };

        db.users.Add(entity);
        await db.SaveChangesAsync(ct);
        return entity.user_id;
    }

    public async Task AssignRoleAsync(Guid userId, Guid roleId, CancellationToken ct)
    {
        var linking = new user_role
        {
            user_role_id = Guid.NewGuid(),
            user_id = userId,
            role_id = roleId,
        };
        db.user_roles.Add(linking);
        await db.SaveChangesAsync(ct);
    }
}
