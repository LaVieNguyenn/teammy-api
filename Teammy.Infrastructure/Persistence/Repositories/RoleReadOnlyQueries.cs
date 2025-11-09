using Microsoft.EntityFrameworkCore;
using Teammy.Application.Common.Interfaces;
using Teammy.Infrastructure.Persistence;

namespace Teammy.Infrastructure.Persistence.Repositories;

public sealed class RoleReadOnlyQueries(AppDbContext db) : IRoleReadOnlyQueries
{
    public async Task<IReadOnlyList<string>> GetAllRoleNamesAsync(CancellationToken ct)
        => await db.roles.AsNoTracking().OrderBy(r => r.name).Select(r => r.name).ToListAsync(ct);

    public async Task<Guid?> GetRoleIdByNameAsync(string roleName, CancellationToken ct)
        => await db.roles.AsNoTracking()
               .Where(r => r.name.ToLower() == roleName.ToLower())
               .Select(r => (Guid?)r.role_id)
               .FirstOrDefaultAsync(ct);
}
