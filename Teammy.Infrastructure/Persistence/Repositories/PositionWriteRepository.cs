using Microsoft.EntityFrameworkCore;
using Teammy.Application.Common.Interfaces;
using Teammy.Infrastructure.Persistence.Models;

namespace Teammy.Infrastructure.Persistence.Repositories;

public sealed class PositionWriteRepository(AppDbContext db) : IPositionWriteRepository
{
    public async Task<Guid> CreateAsync(Guid majorId, string positionName, CancellationToken ct)
    {
        if (majorId == Guid.Empty)
            throw new ArgumentException("majorId is required", nameof(majorId));
        if (string.IsNullOrWhiteSpace(positionName))
            throw new ArgumentException("positionName is required", nameof(positionName));

        var normalized = positionName.Trim();
        var exists = await db.position_lists
            .AnyAsync(p => p.major_id == majorId && p.position_name.ToLower() == normalized.ToLower(), ct);
        if (exists)
            throw new InvalidOperationException("Position already exists for this major.");

        var entity = new position_list
        {
            position_id = Guid.NewGuid(),
            major_id = majorId,
            position_name = normalized,
            created_at = DateTime.UtcNow
        };

        db.position_lists.Add(entity);
        await db.SaveChangesAsync(ct);
        return entity.position_id;
    }

    public async Task UpdateAsync(Guid positionId, Guid majorId, string positionName, CancellationToken ct)
    {
        if (positionId == Guid.Empty)
            throw new ArgumentException("positionId is required", nameof(positionId));
        if (majorId == Guid.Empty)
            throw new ArgumentException("majorId is required", nameof(majorId));
        if (string.IsNullOrWhiteSpace(positionName))
            throw new ArgumentException("positionName is required", nameof(positionName));

        var entity = await db.position_lists
            .FirstOrDefaultAsync(p => p.position_id == positionId, ct)
            ?? throw new KeyNotFoundException("Position not found.");

        var normalized = positionName.Trim();
        var exists = await db.position_lists
            .AnyAsync(p => p.position_id != positionId
                           && p.major_id == majorId
                           && p.position_name.ToLower() == normalized.ToLower(), ct);
        if (exists)
            throw new InvalidOperationException("Position already exists for this major.");

        entity.major_id = majorId;
        entity.position_name = normalized;
        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(Guid positionId, CancellationToken ct)
    {
        var entity = await db.position_lists
            .FirstOrDefaultAsync(p => p.position_id == positionId, ct);
        if (entity is null)
            return;

        db.position_lists.Remove(entity);
        await db.SaveChangesAsync(ct);
    }
}
