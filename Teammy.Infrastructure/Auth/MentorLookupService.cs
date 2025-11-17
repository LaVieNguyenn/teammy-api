using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Teammy.Application.Common.Interfaces;
using Teammy.Infrastructure.Persistence;

namespace Teammy.Infrastructure.Auth
{
    public sealed class MentorLookupService : IMentorLookupService
    {
        private readonly AppDbContext _db;

        public MentorLookupService(AppDbContext db)
        {
            _db = db;
        }

        public async Task<Guid> GetMentorIdByEmailAsync(string email, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(email))
                throw new ArgumentException("Mentor email is required.", nameof(email));

            var normalized = email.Trim().ToLowerInvariant();

            var mentor =
                await (from u in _db.users
                       join ur in _db.user_roles on u.user_id equals ur.user_id
                       join r in _db.roles on ur.role_id equals r.role_id
                       where u.is_active
                             && u.email.ToLower() == normalized
                             && r.name == "mentor"
                       select u)
                .FirstOrDefaultAsync(ct);

            if (mentor is null)
                throw new InvalidOperationException(
                    $"Mentor with email '{email}' not found or not a mentor.");

            return mentor.user_id;
        }
    }
}
