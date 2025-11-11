using Microsoft.EntityFrameworkCore;
using Teammy.Application.Common.Interfaces;
using Teammy.Domain.Users;
using Teammy.Infrastructure.Mapping;

namespace Teammy.Infrastructure.Persistence.Repositories;

public sealed class UserRepository(AppDbContext db) : IUserRepository
{
    public async Task<User?> FindActiveByEmailAsync(string email, CancellationToken ct)
    {
        var query =
            from u in db.users
            join ur in db.user_roles on u.user_id equals ur.user_id
            join r in db.roles on ur.role_id equals r.role_id
            where u.email == email && u.is_active
            select new { u, RoleName = r.name };

        var row = await query.AsNoTracking().FirstOrDefaultAsync(ct);
        return row is null ? null : PersistenceToDomainMapper.ToDomainUser(row.u, row.RoleName!);
    }

    public async Task<User?> FindByIdAsync(Guid id, CancellationToken ct)
    {
        var query =
            from u in db.users
            join ur in db.user_roles on u.user_id equals ur.user_id
            join r in db.roles on ur.role_id equals r.role_id
            where u.user_id == id
            select new { u, RoleName = r.name };

        var row = await query.AsNoTracking().FirstOrDefaultAsync(ct);
        return row is null ? null : PersistenceToDomainMapper.ToDomainUser(row.u, row.RoleName!);
    }

    public async Task UpdateAsync(User user, CancellationToken ct)
    {
        var ef = await db.users.FirstAsync(x => x.user_id == user.Id, ct);
        ef.display_name = user.DisplayName;
        ef.email_verified = user.EmailVerified;
        ef.avatar_url = user.AvatarUrl;
        ef.updated_at = DateTime.Now;
        await db.SaveChangesAsync(ct);
    }
}
