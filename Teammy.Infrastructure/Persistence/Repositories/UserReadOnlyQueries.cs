using Microsoft.EntityFrameworkCore;
using System;
using System.Threading;
using System.Threading.Tasks;
using Teammy.Application.Auth.Dtos;
using Teammy.Application.Common.Interfaces;
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
    }
}
