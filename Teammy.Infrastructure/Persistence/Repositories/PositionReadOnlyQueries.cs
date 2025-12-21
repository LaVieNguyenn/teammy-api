using Microsoft.EntityFrameworkCore;
using Teammy.Application.Common.Interfaces;
using Teammy.Infrastructure.Persistence;

namespace Teammy.Infrastructure.Persistence.Repositories;

public sealed class PositionReadOnlyQueries : IPositionReadOnlyQueries
{
    private readonly AppDbContext _db;

    public PositionReadOnlyQueries(AppDbContext db)
    {
        _db = db;
    }

    public Task<Guid?> FindPositionIdByNameAsync(Guid majorId, string positionName, CancellationToken ct)
    {
        if (majorId == Guid.Empty) return Task.FromResult<Guid?>(null);
        if (string.IsNullOrWhiteSpace(positionName)) return Task.FromResult<Guid?>(null);

        var normalized = positionName.Trim().ToLowerInvariant();
        return _db.position_lists.AsNoTracking()
            .Where(p => p.major_id == majorId && p.position_name.ToLower() == normalized)
            .Select(p => (Guid?)p.position_id)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<IReadOnlyList<(Guid PositionId, string PositionName)>> ListByMajorAsync(Guid majorId, CancellationToken ct)
    {
        if (majorId == Guid.Empty) return Array.Empty<(Guid, string)>();

        var rows = await _db.position_lists.AsNoTracking()
            .Where(p => p.major_id == majorId)
            .OrderBy(p => p.position_name)
            .Select(p => new { p.position_id, p.position_name })
            .ToListAsync(ct);

        return rows.Select(r => (r.position_id, r.position_name)).ToList();
    }
}
