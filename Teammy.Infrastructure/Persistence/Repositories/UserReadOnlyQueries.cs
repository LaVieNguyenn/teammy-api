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
            var user = await _db.users.AsNoTracking()
                .Include(u => u.major)
                .FirstOrDefaultAsync(u => u.user_id == userId, ct);

            if (user is null) return null;

            var roles = await (from ur in _db.user_roles.AsNoTracking()
                               join r in _db.roles.AsNoTracking() on ur.role_id equals r.role_id
                               where ur.user_id == userId
                               select new UserRoleDto(ur.role_id, r.name))
                .ToListAsync(ct);

            var primaryRole = roles.Count > 0 ? roles[0].Name : "student";

            JsonElement? skills = null;
            if (!string.IsNullOrWhiteSpace(user.skills))
            {
                try
                {
                    using var doc = JsonDocument.Parse(user.skills);
                    skills = doc.RootElement.Clone();
                }
                catch
                {
                    // ignore malformed json
                }
            }

            var major = user.major is null
                ? null
                : new MajorSummaryDto(user.major.major_id, user.major.major_name);

            return new CurrentUserDto(
                user.user_id,
                user.email!,
                user.display_name!,
                user.avatar_url,
                user.email_verified,
                user.skills_completed,
                user.is_active,
                primaryRole,
                roles,
                user.phone,
                user.student_code,
                user.gender,
                user.major_id,
                major,
                skills,
                user.created_at,
                user.updated_at,
                user.portfolio_url
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
                row.User.avatar_url,
                row.User.portfolio_url
            );
        }

        public async Task<IReadOnlyList<UserSearchDto>> SearchInvitableAsync(
            string? query,
            Guid semesterId,
            Guid? majorId,
            bool requireStudentRole,
            int limit,
            CancellationToken ct)
        {
            if (limit <= 0 || limit > 100)
                limit = 20;

            var term = (query ?? string.Empty).Trim();
            var statuses = new[] { "pending", "member", "leader" };

            var q =
                from u in _db.users.AsNoTracking()
                join ur in _db.user_roles.AsNoTracking() on u.user_id equals ur.user_id into urj
                from ur in urj.DefaultIfEmpty()
                join r in _db.roles.AsNoTracking() on ur.role_id equals r.role_id into rj
                from r in rj.DefaultIfEmpty()
                where u.is_active
                where !majorId.HasValue || u.major_id == majorId
                where !requireStudentRole || (r != null && r.name == "student")
                where string.IsNullOrEmpty(term)
                      || EF.Functions.ILike(u.email, "%" + term + "%")
                      || EF.Functions.ILike(u.display_name!, "%" + term + "%")
                orderby u.display_name
                select new UserSearchDto(
                    u.user_id,
                    u.email!,
                    u.display_name!,
                    u.avatar_url,
                    u.email_verified,
                    u.major_id,
                    _db.group_members.Any(m =>
                        m.user_id == u.user_id
                        && m.semester_id == semesterId
                        && statuses.Contains(m.status))
                );

            var result = await q.Distinct().Take(limit).ToListAsync(ct);
            return result;
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
                    u.created_at,
                    u.portfolio_url
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
                    u.updated_at,
                    u.portfolio_url
                );

            return await q.AsNoTracking().FirstOrDefaultAsync(ct);
        }
        public async Task<AdminUserDetailDto?> GetByDisplayNameAsync(string displayName, CancellationToken ct)
        {
            var q =
                from u in _db.users.AsNoTracking()
                join ur in _db.user_roles.AsNoTracking() on u.user_id equals ur.user_id
                join r in _db.roles.AsNoTracking() on ur.role_id equals r.role_id
                join m in _db.majors on u.major_id equals m.major_id into mj
                from m in mj.DefaultIfEmpty()
                where u.display_name.ToLower() == displayName.ToLower()
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
                    u.updated_at,
                    u.portfolio_url
                );

            return await q.AsNoTracking().FirstOrDefaultAsync(ct);
        }

        public async Task<AdminUserDetailDto?> GetByStudentCodeAsync(string studentCode, CancellationToken ct)
        {
            var q =
                from u in _db.users.AsNoTracking()
                join ur in _db.user_roles.AsNoTracking() on u.user_id equals ur.user_id
                join r in _db.roles.AsNoTracking() on ur.role_id equals r.role_id
                join m in _db.majors on u.major_id equals m.major_id into mj
                from m in mj.DefaultIfEmpty()
                where u.student_code == studentCode
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
                    u.updated_at,
                    u.portfolio_url
                );

            return await q.AsNoTracking().FirstOrDefaultAsync(ct);
        }
        public async Task<IReadOnlyList<AdminMajorStatsDto>> GetMajorStatsAsync(Guid semesterId, CancellationToken ct)
        {
            var activeStatuses = new[] { "member", "leader" };

            var majors = await _db.majors.AsNoTracking()
                .OrderBy(m => m.major_name)
                .Select(m => new { m.major_id, Name = m.major_name ?? "Unknown" })
                .ToListAsync(ct);

            if (majors.Count == 0)
                return Array.Empty<AdminMajorStatsDto>();

            var groupsByMajor = await (
                from g in _db.groups.AsNoTracking()
                where g.semester_id == semesterId && g.major_id != null
                group g by g.major_id!.Value into gg
                select new
                {
                    MajorId = gg.Key,
                    Count = gg.Count()
                }).ToDictionaryAsync(x => x.MajorId, x => x.Count, ct);

            var studentBase =
                from u in _db.users.AsNoTracking()
                join ur in _db.user_roles.AsNoTracking() on u.user_id equals ur.user_id
                join r in _db.roles.AsNoTracking() on ur.role_id equals r.role_id
                where r.name == "student" && u.major_id != null
                select new { u.user_id, MajorId = u.major_id!.Value };

            var studentsByMajor = await (
                from s in studentBase
                group s by s.MajorId into sg
                select new
                {
                    MajorId = sg.Key,
                    Count = sg.Select(x => x.user_id).Distinct().Count()
                }).ToDictionaryAsync(x => x.MajorId, x => x.Count, ct);

            var members = _db.group_members
                .AsNoTracking()
                .Where(m => m.semester_id == semesterId && activeStatuses.Contains(m.status));

            var studentsWithoutGroup = await (
                from s in studentBase
                join gm in members on s.user_id equals gm.user_id into gmj
                from gm in gmj.DefaultIfEmpty()
                where gm == null
                group s by s.MajorId into sg
                select new
                {
                    MajorId = sg.Key,
                    Count = sg.Select(x => x.user_id).Distinct().Count()
                }).ToDictionaryAsync(x => x.MajorId, x => x.Count, ct);

            var result = majors
                .Select(m => new AdminMajorStatsDto(
                    m.major_id,
                    m.Name,
                    groupsByMajor.TryGetValue(m.major_id, out var gCount) ? gCount : 0,
                    studentsByMajor.TryGetValue(m.major_id, out var sCount) ? sCount : 0,
                    studentsWithoutGroup.TryGetValue(m.major_id, out var swgCount) ? swgCount : 0))
                .ToList();

            return result;
        }
    }
}
