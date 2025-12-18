using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Teammy.Infrastructure.Ai;
using Teammy.Infrastructure.Persistence;
using Teammy.Infrastructure.Persistence.Models;

namespace Teammy.Infrastructure.Ai.Indexing;

public sealed class AiIndexOutboxSaveChangesInterceptor : SaveChangesInterceptor
{
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        if (eventData.Context is not AppDbContext db)
            return base.SavingChangesAsync(eventData, result, cancellationToken);

        var entries = db.ChangeTracker.Entries()
            .Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            .ToList();

        foreach (var entry in entries)
        {
            switch (entry.Entity)
            {
                case topic:
                    HandleTopic(db, entry);
                    break;
                case recruitment_post:
                    HandleRecruitmentPost(db, entry);
                    break;
            }
        }

        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private static void HandleTopic(AppDbContext db, EntityEntry entry)
    {
        var semesterId = ReadGuid(entry, nameof(topic.semester_id));
        if (semesterId == Guid.Empty)
            return;

        var entityId = ReadGuid(entry, nameof(topic.topic_id));
        if (entityId == Guid.Empty)
            return;

        var majorId = ReadNullableGuid(entry, nameof(topic.major_id));
        var status = ReadString(entry, nameof(topic.status));
        var action = ResolveAction(entry.State, status, treatClosedAsDelete: true);
        Enqueue(db, "topic", entityId, semesterId, majorId, action);
    }

    private static void HandleRecruitmentPost(AppDbContext db, EntityEntry entry)
    {
        var entity = (recruitment_post)entry.Entity;
        var postType = ReadString(entry, nameof(recruitment_post.post_type)) ?? entity.post_type;
        var type = postType switch
        {
            "group_hiring" => "recruitment_post",
            "individual" => "profile_post",
            _ => null
        };

        if (type is null)
            return;

        var entityId = ReadGuid(entry, nameof(recruitment_post.post_id));
        if (entityId == Guid.Empty)
            return;

        var semesterId = ReadGuid(entry, nameof(recruitment_post.semester_id));
        if (semesterId == Guid.Empty)
            return;

        var majorId = ReadNullableGuid(entry, nameof(recruitment_post.major_id));
        var status = ReadString(entry, nameof(recruitment_post.status));
        var action = ResolveAction(entry.State, status, treatClosedAsDelete: true);

        Enqueue(db, type, entityId, semesterId, majorId, action);
    }

    private static AiIndexAction ResolveAction(EntityState state, string? status, bool treatClosedAsDelete)
    {
        if (state == EntityState.Deleted)
            return AiIndexAction.Delete;

        if (!treatClosedAsDelete)
            return AiIndexAction.Upsert;

        if (string.IsNullOrWhiteSpace(status))
            return AiIndexAction.Upsert;

        var normalized = status.Trim().ToLowerInvariant();
        return normalized is "closed" or "archived"
            ? AiIndexAction.Delete
            : AiIndexAction.Upsert;
    }

    private static void Enqueue(AppDbContext db, string type, Guid entityId, Guid semesterId, Guid? majorId, AiIndexAction action)
    {
        var set = db.Set<AiIndexOutboxItem>();

        var exists = set.Any(x => x.ProcessedAtUtc == null && x.Type == type && x.EntityId == entityId && x.Action == action);
        if (exists)
            return;

        set.Add(new AiIndexOutboxItem
        {
            Type = type,
            EntityId = entityId,
            SemesterId = semesterId,
            MajorId = majorId,
            PointId = AiPointId.Stable(type, entityId),
            Action = action
        });
    }

    private static Guid ReadGuid(EntityEntry entry, string propertyName)
    {
        var property = entry.Property(propertyName);
        var value = entry.State == EntityState.Deleted ? property.OriginalValue : property.CurrentValue;
        return value is Guid guid ? guid : Guid.Empty;
    }

    private static Guid? ReadNullableGuid(EntityEntry entry, string propertyName)
    {
        var property = entry.Property(propertyName);
        var value = entry.State == EntityState.Deleted ? property.OriginalValue : property.CurrentValue;
        return value as Guid?;
    }

    private static string? ReadString(EntityEntry entry, string propertyName)
    {
        var property = entry.Property(propertyName);
        var value = entry.State == EntityState.Deleted ? property.OriginalValue : property.CurrentValue;
        return value as string;
    }
}
