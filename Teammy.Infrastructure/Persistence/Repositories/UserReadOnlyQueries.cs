using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Teammy.Application.Auth.Dtos;
using Teammy.Application.Common.Interfaces;
using Teammy.Application.Users.Dtos;
using Teammy.Infrastructure.Persistence;

namespace Teammy.Infrastructure.Persistence.Repositories
{
    public sealed class UserReadOnlyQueries : IUserReadOnlyQueries
    {
        private readonly AppDbContext _db;
        public UserReadOnlyQueries(AppDbContext db) => _db = db;

        public Task<CurrentUserDto?> GetCurrentUserAsync(Guid userId, CancellationToken ct)
        {
            var q =
                from u in _db.users
                join ur in _db.user_roles on u.user_id equals ur.user_id
                join r in _db.roles on ur.role_id equals r.role_id
                where u.user_id == userId
                select new CurrentUserDto(
                    u.user_id, u.email!, u.display_name!, r.name!, u.avatar_url, u.email_verified, u.skills_completed
                );

            return q.AsNoTracking().FirstOrDefaultAsync(ct);
        }

        public Task<IReadOnlyList<UserSearchDto>> SearchInvitableAsync(string? query, Guid semesterId, int limit, CancellationToken ct)
        {
            if (limit <= 0 || limit > 100) limit = 20;
            var term = (query ?? string.Empty).Trim();
            var statuses = new[] { "pending", "member", "leader" };

            var q = _db.users.AsNoTracking()
                .Where(u => u.is_active)
                .Where(u => string.IsNullOrEmpty(term)
                    || u.display_name.Contains(term)
                    || u.email.Contains(term)
                    || ((u.skills ?? string.Empty).Contains(term)))
                .OrderBy(u => u.display_name)
                .Select(u => new UserSearchDto(
                    u.user_id,
                    u.email!,
                    u.display_name!,
                    u.avatar_url,
                    u.email_verified,
                    u.major_id,
                    _db.group_members.Any(m => m.user_id == u.user_id && m.semester_id == semesterId && statuses.Contains(m.status))
                ))
                .Take(limit);

            return q.ToListAsync(ct).ContinueWith(t => (IReadOnlyList<UserSearchDto>)t.Result, ct);
        }
    }
}
