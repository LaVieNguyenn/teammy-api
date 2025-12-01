using Microsoft.EntityFrameworkCore;
using Teammy.Application.Common.Interfaces;
using Teammy.Application.Dashboard.Dtos;
using Teammy.Infrastructure.Persistence;

namespace Teammy.Infrastructure.Persistence.Repositories;

public sealed class DashboardReadOnlyQueries(AppDbContext db) : IDashboardReadOnlyQueries
{
    public async Task<DashboardStatsDto> GetStatsAsync(CancellationToken ct)
    {
        return new DashboardStatsDto(
            await db.users.CountAsync(ct),
            await db.users.CountAsync(u => u.is_active, ct),
            await db.topics.CountAsync(ct),
            await db.topics.CountAsync(t => t.status == "open", ct),
            await db.groups.CountAsync(ct),
            await db.groups.CountAsync(g => g.status == "recruiting", ct),
            await db.groups.CountAsync(g => g.status == "active", ct),
            await db.recruitment_posts.CountAsync(ct),
            await db.recruitment_posts.CountAsync(p => p.post_type == "group_hiring", ct),
            await db.recruitment_posts.CountAsync(p => p.post_type == "individual", ct));
    }
}
