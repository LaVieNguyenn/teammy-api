using Microsoft.EntityFrameworkCore;
using Teammy.Application.Activity.Dtos;
using Teammy.Application.Common.Interfaces;
using Teammy.Infrastructure.Persistence;
using Teammy.Infrastructure.Persistence.Models;

namespace Teammy.Infrastructure.Persistence.Repositories;

public sealed class ActivityLogRepository(AppDbContext db) : IActivityLogRepository
{
    private readonly AppDbContext _db = db;

    public async Task<ActivityLogDto> InsertAsync(ActivityLogRecord record, CancellationToken ct)
    {
        var entity = new activity_log
        {
            activity_id = Guid.NewGuid(),
            actor_id = record.ActorId,
            action = record.Action,
            entity_type = record.EntityType,
            entity_id = record.EntityId,
            group_id = record.GroupId,
            target_user_id = record.TargetUserId,
            message = record.Message,
            metadata = record.MetadataJson,
            status = string.IsNullOrWhiteSpace(record.Status) ? "success" : record.Status,
            platform = record.Platform,
            severity = record.Severity,
            created_at = DateTime.UtcNow
        };

        _db.activity_logs.Add(entity);
        await _db.SaveChangesAsync(ct);

        return await BuildQueryById(entity.activity_id)
            .FirstAsync(ct);
    }

    public async Task<IReadOnlyList<ActivityLogDto>> ListAsync(ActivityLogListRequest request, CancellationToken ct)
    {
        var baseQuery = _db.activity_logs.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(request.EntityType))
            baseQuery = baseQuery.Where(x => x.entity_type == request.EntityType);

        if (!string.IsNullOrWhiteSpace(request.Action))
            baseQuery = baseQuery.Where(x => x.action == request.Action);

        if (request.GroupId.HasValue)
            baseQuery = baseQuery.Where(x => x.group_id == request.GroupId);

        if (request.Before.HasValue)
            baseQuery = baseQuery.Where(x => x.created_at < request.Before.Value);

        if (request.StartUtc.HasValue)
            baseQuery = baseQuery.Where(x => x.created_at >= request.StartUtc.Value);

        if (request.EndUtc.HasValue)
            baseQuery = baseQuery.Where(x => x.created_at < request.EndUtc.Value);

        var limit = request.GetEffectiveLimit();

        baseQuery = baseQuery.OrderByDescending(x => x.created_at);

        return await Project(baseQuery)
            .Take(limit)
            .ToListAsync(ct);
    }

    private IQueryable<ActivityLogDto> BuildQuery()
        => Project(_db.activity_logs.AsNoTracking());

    private IQueryable<ActivityLogDto> BuildQueryById(Guid id)
        => Project(_db.activity_logs.AsNoTracking().Where(log => log.activity_id == id));

    private IQueryable<ActivityLogDto> Project(IQueryable<activity_log> source)
        => from log in source
           join actor in _db.users.AsNoTracking() on log.actor_id equals actor.user_id into actorJoin
           from actor in actorJoin.DefaultIfEmpty()
           join target in _db.users.AsNoTracking() on log.target_user_id equals target.user_id into targetJoin
           from target in targetJoin.DefaultIfEmpty()
           select new ActivityLogDto(
               log.activity_id,
               log.group_id,
               log.entity_type,
               log.entity_id,
               log.action,
               log.actor_id,
               actor != null ? actor.display_name : null,
               actor != null ? actor.email : null,
               log.target_user_id,
               target != null ? target.display_name : null,
               log.message,
               log.metadata,
               log.status,
               log.platform,
               log.severity,
               log.created_at);
}
