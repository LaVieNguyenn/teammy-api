using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Teammy.Application.ProjectTracking.Dtos;
using Teammy.Application.ProjectTracking.Interfaces;
using Teammy.Infrastructure.Persistence;
using Teammy.Infrastructure.Persistence.Models;

namespace Teammy.Infrastructure.Persistence.Repositories;

public sealed class ProjectTrackingRepository(AppDbContext db) : IProjectTrackingRepository
{
    private static readonly HashSet<string> BacklogStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "planned","ready","in_progress","blocked","completed","archived"
    };

    private static readonly HashSet<string> MilestoneStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "planned","in_progress","completed","blocked","archived","slipped"
    };

    public async Task<Guid> CreateBacklogItemAsync(Guid groupId, Guid createdBy, CreateBacklogItemRequest req, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var entity = new backlog_item
        {
            backlog_item_id = Guid.NewGuid(),
            group_id = groupId,
            title = req.Title,
            description = req.Description,
            priority = req.Priority,
            category = req.Category,
            story_points = req.StoryPoints,
            owner_user_id = req.OwnerUserId,
            created_by = createdBy,
            due_date = req.DueDate,
            status = "planned",
            created_at = now,
            updated_at = now
        };

        db.backlog_items.Add(entity);
        await db.SaveChangesAsync(ct);
        return entity.backlog_item_id;
    }

    public async Task UpdateBacklogItemAsync(Guid backlogItemId, Guid groupId, UpdateBacklogItemRequest req, CancellationToken ct)
    {
        var entity = await db.backlog_items.FirstOrDefaultAsync(b => b.backlog_item_id == backlogItemId, ct)
                    ?? throw new KeyNotFoundException("Backlog item not found");
        if (entity.group_id != groupId)
            throw new UnauthorizedAccessException("Backlog item does not belong to this group");

        var status = NormalizeBacklogStatus(req.Status);

        entity.title = req.Title;
        entity.description = req.Description;
        entity.priority = req.Priority;
        entity.category = req.Category;
        entity.story_points = req.StoryPoints;
        entity.owner_user_id = req.OwnerUserId;
        entity.due_date = req.DueDate;
        entity.status = status;
        entity.updated_at = DateTime.UtcNow;

        await db.SaveChangesAsync(ct);
    }

    public async Task ArchiveBacklogItemAsync(Guid backlogItemId, Guid groupId, CancellationToken ct)
    {
        var entity = await db.backlog_items
            .Include(b => b.milestone_items)
            .FirstOrDefaultAsync(b => b.backlog_item_id == backlogItemId, ct)
            ?? throw new KeyNotFoundException("Backlog item not found");
        if (entity.group_id != groupId)
            throw new UnauthorizedAccessException("Backlog item does not belong to this group");

        db.milestone_items.RemoveRange(entity.milestone_items);
        db.backlog_items.Remove(entity);
        await db.SaveChangesAsync(ct);
    }

    public async Task<Guid> CreateMilestoneAsync(Guid groupId, Guid createdBy, CreateMilestoneRequest req, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var entity = new milestone
        {
            milestone_id = Guid.NewGuid(),
            group_id = groupId,
            name = req.Name,
            description = req.Description,
            target_date = req.TargetDate,
            status = "planned",
            completed_at = null,
            created_by = createdBy,
            created_at = now,
            updated_at = now
        };

        db.milestones.Add(entity);
        await db.SaveChangesAsync(ct);
        return entity.milestone_id;
    }

    public async Task UpdateMilestoneAsync(Guid milestoneId, Guid groupId, UpdateMilestoneRequest req, CancellationToken ct)
    {
        var entity = await db.milestones.FirstOrDefaultAsync(m => m.milestone_id == milestoneId, ct)
                    ?? throw new KeyNotFoundException("Milestone not found");
        if (entity.group_id != groupId)
            throw new UnauthorizedAccessException("Milestone does not belong to this group");

        entity.name = req.Name;
        entity.description = req.Description;
        entity.target_date = req.TargetDate;
        entity.status = NormalizeMilestoneStatus(req.Status);
        entity.completed_at = req.CompletedAt;
        entity.updated_at = DateTime.UtcNow;

        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteMilestoneAsync(Guid milestoneId, Guid groupId, CancellationToken ct)
    {
        var entity = await db.milestones.Include(m => m.milestone_items)
            .FirstOrDefaultAsync(m => m.milestone_id == milestoneId, ct)
            ?? throw new KeyNotFoundException("Milestone not found");
        if (entity.group_id != groupId)
            throw new UnauthorizedAccessException("Milestone does not belong to this group");

        db.milestone_items.RemoveRange(entity.milestone_items);
        db.milestones.Remove(entity);
        await db.SaveChangesAsync(ct);
    }

    public async Task AssignMilestoneItemsAsync(Guid milestoneId, Guid groupId, IReadOnlyList<Guid> backlogItemIds, CancellationToken ct)
    {
        var milestone = await db.milestones.FirstOrDefaultAsync(m => m.milestone_id == milestoneId, ct)
                        ?? throw new KeyNotFoundException("Milestone not found");
        if (milestone.group_id != groupId)
            throw new UnauthorizedAccessException("Milestone does not belong to this group");

        var ids = backlogItemIds.Where(id => id != Guid.Empty).Distinct().ToArray();
        if (ids.Length == 0) return;

        var backlogItems = await db.backlog_items.Where(b => ids.Contains(b.backlog_item_id)).ToListAsync(ct);
        if (backlogItems.Count != ids.Length)
            throw new KeyNotFoundException("One or more backlog items were not found");
        if (backlogItems.Any(b => b.group_id != groupId))
            throw new UnauthorizedAccessException("All backlog items must belong to the same group");

        var existingLinks = await db.milestone_items.Where(mi => ids.Contains(mi.backlog_item_id)).ToListAsync(ct);
        db.milestone_items.RemoveRange(existingLinks.Where(mi => mi.milestone_id != milestoneId));

        var now = DateTime.UtcNow;
        var missing = ids.Except(existingLinks.Where(mi => mi.milestone_id == milestoneId).Select(mi => mi.backlog_item_id)).ToArray();
        foreach (var backlogId in missing)
        {
            db.milestone_items.Add(new milestone_item
            {
                milestone_item_id = Guid.NewGuid(),
                milestone_id = milestoneId,
                backlog_item_id = backlogId,
                added_at = now
            });
        }

        await db.SaveChangesAsync(ct);
    }

    public async Task RemoveMilestoneItemAsync(Guid milestoneId, Guid backlogItemId, Guid groupId, CancellationToken ct)
    {
        var milestone = await db.milestones.FirstOrDefaultAsync(m => m.milestone_id == milestoneId, ct)
                        ?? throw new KeyNotFoundException("Milestone not found");
        if (milestone.group_id != groupId)
            throw new UnauthorizedAccessException("Milestone does not belong to this group");

        var link = await db.milestone_items
            .FirstOrDefaultAsync(mi => mi.milestone_id == milestoneId && mi.backlog_item_id == backlogItemId, ct);
        if (link is null) return;

        db.milestone_items.Remove(link);
        await db.SaveChangesAsync(ct);
    }

    private static string NormalizeBacklogStatus(string input)
    {
        var status = input?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(status) || !BacklogStatuses.Contains(status))
            throw new ArgumentException("Invalid backlog status", nameof(input));
        return status;
    }

    private static string NormalizeMilestoneStatus(string input)
    {
        var status = input?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(status) || !MilestoneStatuses.Contains(status))
            throw new ArgumentException("Invalid milestone status", nameof(input));
        return status;
    }
}
