using Microsoft.EntityFrameworkCore;
using Teammy.Application.Kanban.Interfaces;
using Teammy.Infrastructure.Persistence;

namespace Teammy.Infrastructure.Persistence.Repositories;

public sealed class GroupAccessQueries(AppDbContext db) : IGroupAccessQueries
{
    public Task<bool> IsMemberAsync(Guid groupId, Guid userId, CancellationToken ct)
        => db.group_members.AsNoTracking()
           .AnyAsync(m => m.group_id == groupId && m.user_id == userId && (m.status == "member" || m.status == "leader"), ct);

    public Task<bool> IsLeaderAsync(Guid groupId, Guid userId, CancellationToken ct)
        => db.group_members.AsNoTracking()
           .AnyAsync(m => m.group_id == groupId && m.user_id == userId && m.status == "leader", ct);
}
