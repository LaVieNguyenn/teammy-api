using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json;
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

        public async Task<CurrentUserDto?> GetCurrentUserAsync(Guid userId, CancellationToken ct)
        {
            var query =
                from u in _db.users
                join ur in _db.user_roles on u.user_id equals ur.user_id
                join r in _db.roles on ur.role_id equals r.role_id
                where u.user_id == userId
                select new
                {
                    User = u,
                    RoleName = r.name
                };

            var row = await query.AsNoTracking().FirstOrDefaultAsync(ct);
            if (row is null) return null;

            return new CurrentUserDto(
                row.User.user_id,
                row.User.email!,
                row.User.display_name!,
                row.RoleName!,
                row.User.avatar_url,
                row.User.email_verified,
                row.User.skills_completed
            );
        }

        public async Task<UserProfileDto?> GetProfileAsync(Guid userId, CancellationToken ct)
        {
            var query =
                from u in _db.users
                join m in _db.majors on u.major_id equals m.major_id into mj
                from m in mj.DefaultIfEmpty()
                where u.user_id == userId
                select new { User = u, MajorName = m.major_name };

            var row = await query.AsNoTracking().FirstOrDefaultAsync(ct);
            if (row is null) return null;

            JsonElement? skills = null;
            if (!string.IsNullOrWhiteSpace(row.User.skills))
            {
                try
                {
                    using var doc = JsonDocument.Parse(row.User.skills);
                    skills = doc.RootElement.Clone();
                }
                catch
                {
                    // ignore malformed json, treat as null
                }
            }

            return new UserProfileDto(
                row.User.user_id,
                row.User.email!,
                row.User.display_name!,
                row.User.phone,
                row.User.gender,
                row.User.student_code,
                row.User.major_id,
                row.MajorName,
                skills,
                row.User.skills_completed,
                row.User.avatar_url
            );
        }

    public Task<IReadOnlyList<UserSearchDto>> SearchInvitableAsync(string? query, Guid semesterId, int limit, CancellationToken ct)
        {
            if (limit <= 0 || limit > 100) limit = 20;
            var term = (query ?? string.Empty).Trim();
            var statuses = new[] { "pending", "member", "leader" };

            var q = _db.users.AsNoTracking()
                .Where(u => u.is_active)
                // Search by email only (case-insensitive, PostgreSQL ILIKE)
                .Where(u => string.IsNullOrEmpty(term) || EF.Functions.ILike(u.email, "%" + term + "%"))
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

        public async Task<IReadOnlyList<AdminUserListItemDto>> GetAllForAdminAsync(CancellationToken ct)
        {
            var q =
                from u in _db.users
                join ur in _db.user_roles on u.user_id equals ur.user_id into urj
                from ur in urj.DefaultIfEmpty()
                join r in _db.roles on ur.role_id equals r.role_id into rj
                from r in rj.DefaultIfEmpty()
                join m in _db.majors on u.major_id equals m.major_id into mj
                from m in mj.DefaultIfEmpty()
                orderby u.created_at descending
                select new AdminUserListItemDto(
                    u.user_id,
                    u.email!,
                    u.display_name!,
                    u.avatar_url,
                    r.name ?? "student",
                    u.email_verified,
                    u.is_active,
                    u.major_id,
                    m.major_name,
                    u.student_code,
                    u.gender,
                    u.created_at
                );

            return await q.AsNoTracking().ToListAsync(ct);
        }

        public async Task<AdminUserDetailDto?> GetAdminDetailAsync(Guid userId, CancellationToken ct)
        {
            var q =
                from u in _db.users
                join ur in _db.user_roles on u.user_id equals ur.user_id into urj
                from ur in urj.DefaultIfEmpty()
                join r in _db.roles on ur.role_id equals r.role_id into rj
                from r in rj.DefaultIfEmpty()
                join m in _db.majors on u.major_id equals m.major_id into mj
                from m in mj.DefaultIfEmpty()
                where u.user_id == userId
                select new AdminUserDetailDto(
                    u.user_id,
                    u.email!,
                    u.display_name!,
                    u.avatar_url,
                    r.name ?? "student",
                    u.email_verified,
                    u.is_active,
                    u.major_id,
                    m.major_name,
                    u.student_code,
                    u.gender,
                    u.skills_completed,
                    u.created_at,
                    u.updated_at
                );

            return await q.AsNoTracking().FirstOrDefaultAsync(ct);
        }
    }
}
