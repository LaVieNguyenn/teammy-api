using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Teammy.Application.ProjectTracking.Dtos;
using Teammy.Application.ProjectTracking.Interfaces;
using Teammy.Infrastructure.Persistence;

namespace Teammy.Infrastructure.Persistence.Repositories;

public sealed class ProjectTrackingReadOnlyQueries(AppDbContext db) : IProjectTrackingReadOnlyQueries
{
    public async Task<IReadOnlyList<BacklogItemVm>> ListBacklogAsync(Guid groupId, CancellationToken ct)
    {
        var rows = await BuildBacklogQuery(groupId)
            .ToListAsync(ct);

        return rows
            .OrderBy(r => StatusOrder(r.Status))
            .ThenBy(r => r.DueDate ?? DateTime.MaxValue)
            .ThenBy(r => r.CreatedAt)
            .Select(MapToVm)
            .ToList();
    }

    public async Task<BacklogItemVm?> GetBacklogItemAsync(Guid backlogItemId, Guid groupId, CancellationToken ct)
    {
        var projection = await BuildBacklogQuery(groupId, backlogItemId)
            .FirstOrDefaultAsync(ct);
        return projection is null ? null : MapToVm(projection);
    }

    public async Task<IReadOnlyList<MilestoneVm>> ListMilestonesAsync(Guid groupId, CancellationToken ct)
    {
        var list = await db.milestones.AsNoTracking()
            .Where(m => m.group_id == groupId)
            .OrderBy(m => m.target_date)
            .ThenBy(m => m.name)
            .Select(m => new
            {
                Milestone = m,
                Total = m.milestone_items.Count(),
                Completed = m.milestone_items.Count(mi => mi.backlog_item.status == "completed"),
                Items = m.milestone_items
                    .Select(mi => new
                    {
                        mi.backlog_item_id,
                        Title = mi.backlog_item.title,
                        Status = mi.backlog_item.status,
                        mi.backlog_item.due_date,
                        Task = mi.backlog_item.tasks
                            .OrderByDescending(t => t.updated_at)
                            .Select(t => new { t.task_id, t.column.column_name, t.column.is_done })
                            .FirstOrDefault()
                    })
                    .ToList()
            })
            .ToListAsync(ct);

        return list.Select(x => new MilestoneVm(
            x.Milestone.milestone_id,
            x.Milestone.group_id,
            x.Milestone.name,
            x.Milestone.status,
            x.Milestone.target_date,
            x.Milestone.completed_at,
            x.Milestone.description,
            x.Total,
            x.Completed,
            CalculatePercent(x.Completed, x.Total),
            x.Milestone.created_at,
            x.Milestone.updated_at,
            x.Items.Count == 0
                ? null
                : x.Items.Select(i => new MilestoneItemStatusVm(
                    i.backlog_item_id,
                    i.Title,
                    i.Status,
                    i.due_date,
                    i.Task?.task_id,
                    i.Task?.column_name,
                    i.Task?.is_done
                )).ToList()
        )).ToList();
    }

    public async Task<MilestoneVm?> GetMilestoneAsync(Guid milestoneId, CancellationToken ct)
    {
        var data = await db.milestones.AsNoTracking()
            .Where(m => m.milestone_id == milestoneId)
            .Select(m => new
            {
                Milestone = m,
                Total = m.milestone_items.Count(),
                Completed = m.milestone_items.Count(mi => mi.backlog_item.status == "completed"),
                Items = m.milestone_items
                    .Select(mi => new
                    {
                        mi.backlog_item_id,
                        Title = mi.backlog_item.title,
                        Status = mi.backlog_item.status,
                        mi.backlog_item.due_date,
                        Task = mi.backlog_item.tasks
                            .OrderByDescending(t => t.updated_at)
                            .Select(t => new { t.task_id, t.column.column_name, t.column.is_done })
                            .FirstOrDefault()
                    })
                    .ToList()
            })
            .FirstOrDefaultAsync(ct);

        if (data is null) return null;

        return new MilestoneVm(
            data.Milestone.milestone_id,
            data.Milestone.group_id,
            data.Milestone.name,
            data.Milestone.status,
            data.Milestone.target_date,
            data.Milestone.completed_at,
            data.Milestone.description,
            data.Total,
            data.Completed,
            CalculatePercent(data.Completed, data.Total),
            data.Milestone.created_at,
            data.Milestone.updated_at,
            data.Items.Count == 0
                ? null
                : data.Items.Select(i => new MilestoneItemStatusVm(
                    i.backlog_item_id,
                    i.Title,
                    i.Status,
                    i.due_date,
                    i.Task?.task_id,
                    i.Task?.column_name,
                    i.Task?.is_done
                )).ToList()
        );
    }

    public async Task<ProjectReportVm> BuildProjectReportAsync(Guid groupId, Guid? milestoneId, CancellationToken ct)
    {
        var project = await db.groups.AsNoTracking()
            .Where(g => g.group_id == groupId)
            .Select(g => new
            {
                g.group_id,
                g.name,
                g.status,
                g.created_at,
                g.updated_at,
                Mentor = g.mentor == null ? null : new { g.mentor.user_id, g.mentor.display_name }
            })
            .FirstOrDefaultAsync(ct)
            ?? throw new KeyNotFoundException("Group not found");

        var leaders = await db.group_members.AsNoTracking()
            .Where(m => m.group_id == groupId && m.status == "leader")
            .OrderBy(m => m.joined_at)
            .Select(m => new MemberProfileVm(m.user_id, m.user.display_name ?? string.Empty))
            .ToListAsync(ct);

        var activeMemberCount = await db.group_members.AsNoTracking()
            .Where(m => m.group_id == groupId && (m.status == "leader" || m.status == "member"))
            .CountAsync(ct);

        var mentorProfile = project.Mentor is null
            ? null
            : new MemberProfileVm(project.Mentor.user_id, project.Mentor.display_name ?? string.Empty);

        var tasksReport = await BuildTaskReportInternalAsync(groupId, milestoneId, ct);

        var snapshot = new TeamSnapshotVm(activeMemberCount, leaders, mentorProfile);

        var nextMilestone = tasksReport.Milestones
            .Where(m => !string.Equals(m.Status, "completed", StringComparison.OrdinalIgnoreCase))
            .OrderBy(m => m.TargetDate ?? DateOnly.MaxValue)
            .ThenBy(m => m.Name)
            .FirstOrDefault();

        var summary = new ProjectSummaryVm(
            project.group_id,
            project.name,
            project.status,
            project.created_at,
            project.updated_at,
            tasksReport.Backlog.CompletionPercent,
            tasksReport.Backlog.Total,
            tasksReport.Backlog.Active,
            tasksReport.Backlog.Completed,
            tasksReport.Backlog.Blocked,
            tasksReport.Backlog.Overdue,
            tasksReport.Backlog.DueSoon,
            tasksReport.Milestones.Count,
            nextMilestone?.TargetDate,
            nextMilestone?.Name,
            nextMilestone?.MilestoneId
        );

        return new ProjectReportVm(summary, tasksReport, snapshot);
    }

    private async Task<TaskReportVm> BuildTaskReportInternalAsync(Guid groupId, Guid? milestoneId, CancellationToken ct)
    {
        var backlogQuery = db.backlog_items.AsNoTracking().Where(b => b.group_id == groupId);
        if (milestoneId is Guid filterId)
        {
            backlogQuery =
                from b in backlogQuery
                join mi in db.milestone_items.AsNoTracking() on b.backlog_item_id equals mi.backlog_item_id
                where mi.milestone_id == filterId
                select b;
        }

        var now = DateTime.UtcNow;
        var dueSoonThreshold = now.AddDays(7);

        var statusCounts = await backlogQuery.GroupBy(_ => 1).Select(g => new
        {
            Total = g.Count(),
            Planned = g.Count(b => b.status == "planned"),
            Ready = g.Count(b => b.status == "ready"),
            InProgress = g.Count(b => b.status == "in_progress"),
            Blocked = g.Count(b => b.status == "blocked"),
            Completed = g.Count(b => b.status == "completed"),
            Archived = g.Count(b => b.status == "archived"),
            Overdue = g.Count(b => b.due_date != null && b.due_date < now && b.status != "completed" && b.status != "archived"),
            DueSoon = g.Count(b => b.due_date != null && b.due_date >= now && b.due_date <= dueSoonThreshold && b.status != "completed" && b.status != "archived")
        }).FirstOrDefaultAsync(ct) ?? new
        {
            Total = 0,
            Planned = 0,
            Ready = 0,
            InProgress = 0,
            Blocked = 0,
            Completed = 0,
            Archived = 0,
            Overdue = 0,
            DueSoon = 0
        };

        var columns = await db.columns.AsNoTracking()
            .Where(c => c.board.group_id == groupId)
            .Select(c => new { c.column_id, c.column_name, c.is_done })
            .ToListAsync(ct);

        var taskQuery = db.tasks.AsNoTracking().Where(t => t.group_id == groupId);
        if (milestoneId is Guid mid)
        {
            taskQuery =
                from t in taskQuery
                join mi in db.milestone_items.AsNoTracking() on t.backlog_item_id equals mi.backlog_item_id
                where mi.milestone_id == mid
                select t;
        }

        var taskCounts = await taskQuery
            .GroupBy(t => t.column_id)
            .Select(g => new { ColumnId = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        var columnProgress = columns.Select(col =>
        {
            var count = taskCounts.FirstOrDefault(x => x.ColumnId == col.column_id)?.Count ?? 0;
            return new ColumnProgressVm(col.column_id, col.column_name, col.is_done, count);
        }).ToList();

        var milestoneQuery = db.milestones.AsNoTracking().Where(m => m.group_id == groupId);
        if (milestoneId is Guid mId)
        {
            milestoneQuery = milestoneQuery.Where(m => m.milestone_id == mId);
        }

        var milestoneProgress = await milestoneQuery
            .Select(m => new
            {
                Milestone = m,
                Total = m.milestone_items.Count(),
                Completed = m.milestone_items.Count(mi => mi.backlog_item.status == "completed"),
                Items = m.milestone_items
                    .Select(mi => new
                    {
                        mi.backlog_item_id,
                        Title = mi.backlog_item.title,
                        Status = mi.backlog_item.status,
                        mi.backlog_item.due_date,
                        Task = mi.backlog_item.tasks
                            .OrderByDescending(t => t.updated_at)
                            .Select(t => new { t.task_id, t.column.column_name, t.column.is_done })
                            .FirstOrDefault()
                    })
                    .ToList()
            })
            .OrderBy(m => m.Milestone.target_date)
            .ThenBy(m => m.Milestone.name)
            .ToListAsync(ct);

        var milestoneVms = milestoneProgress.Select(x => new MilestoneProgressVm(
            x.Milestone.milestone_id,
            x.Milestone.name,
            x.Milestone.status,
            x.Milestone.target_date,
            x.Total,
            x.Completed,
            CalculatePercent(x.Completed, x.Total),
            x.Items.Count == 0
                ? null
                : x.Items.Select(i => new MilestoneItemStatusVm(
                    i.backlog_item_id,
                    i.Title,
                    i.Status,
                    i.due_date,
                    i.Task?.task_id,
                    i.Task?.column_name,
                    i.Task?.is_done
                )).ToList()
        )).ToList();

        var total = statusCounts.Total;
        var notStarted = statusCounts.Planned + statusCounts.Ready;
        var active = statusCounts.InProgress + statusCounts.Blocked;
        var remaining = Math.Max(0, total - statusCounts.Completed - statusCounts.Archived);

        var backlogSummary = new BacklogStatusSummary(
            total,
            statusCounts.Planned,
            statusCounts.Ready,
            statusCounts.InProgress,
            statusCounts.Blocked,
            statusCounts.Completed,
            statusCounts.Archived,
            notStarted,
            active,
            remaining,
            statusCounts.Overdue,
            statusCounts.DueSoon,
            CalculatePercent(statusCounts.Completed, total),
            CalculatePercent(active, total),
            CalculatePercent(notStarted, total),
            CalculatePercent(statusCounts.Blocked, total),
            CalculatePercent(statusCounts.Archived, total)
        );

        return new TaskReportVm(backlogSummary, columnProgress, milestoneVms);
    }

    private IQueryable<BacklogProjection> BuildBacklogQuery(Guid groupId, Guid? backlogItemId = null)
    {
        var baseQuery = db.backlog_items.AsNoTracking()
            .Where(b => b.group_id == groupId);

        if (backlogItemId is Guid id)
            baseQuery = baseQuery.Where(b => b.backlog_item_id == id);

        return from b in baseQuery
               join owner in db.users.AsNoTracking() on b.owner_user_id equals owner.user_id into ownerJoin
               from owner in ownerJoin.DefaultIfEmpty()
               join t in db.tasks.AsNoTracking() on b.backlog_item_id equals t.backlog_item_id into taskJoin
               from t in taskJoin.DefaultIfEmpty()
               join c in db.columns.AsNoTracking() on t.column_id equals c.column_id into columnJoin
               from c in columnJoin.DefaultIfEmpty()
               join mi in db.milestone_items.AsNoTracking() on b.backlog_item_id equals mi.backlog_item_id into milestoneJoin
               from mi in milestoneJoin.DefaultIfEmpty()
               join m in db.milestones.AsNoTracking() on mi.milestone_id equals m.milestone_id into milestoneDetailJoin
               from m in milestoneDetailJoin.DefaultIfEmpty()
               select new BacklogProjection(
                   b.backlog_item_id,
                   b.group_id,
                   b.title,
                   b.description,
                   b.status,
                   b.priority,
                   b.category,
                   b.story_points,
                   b.owner_user_id,
                   owner != null ? owner.display_name : null,
                   b.due_date,
                   t != null ? t.task_id : null,
                   c != null ? c.column_id : null,
                   c != null ? c.column_name : null,
                   c != null && c.is_done,
                   m != null ? m.milestone_id : null,
                   m != null ? m.name : null,
                   b.created_at,
                   b.updated_at
               );
    }

    private static BacklogItemVm MapToVm(BacklogProjection row)
        => new(
            row.BacklogItemId,
            row.GroupId,
            row.Title,
            row.Description,
            row.Status,
            row.Priority,
            row.Category,
            row.StoryPoints,
            row.OwnerUserId,
            row.OwnerName,
            row.DueDate,
            row.LinkedTaskId,
            row.ColumnId,
            row.ColumnName,
            row.ColumnIsDone,
            row.MilestoneId,
            row.MilestoneName,
            row.CreatedAt,
            row.UpdatedAt
        );

    private static int StatusOrder(string status) => status switch
    {
        "planned" => 1,
        "ready" => 2,
        "in_progress" => 3,
        "blocked" => 4,
        "completed" => 5,
        "archived" => 6,
        _ => 7
    };

    private static decimal CalculatePercent(int completed, int total)
        => total == 0 ? 0 : Math.Round((decimal)completed / total * 100m, 2);

    private sealed record BacklogProjection(
        Guid BacklogItemId,
        Guid GroupId,
        string Title,
        string? Description,
        string Status,
        string? Priority,
        string? Category,
        int? StoryPoints,
        Guid? OwnerUserId,
        string? OwnerName,
        DateTime? DueDate,
        Guid? LinkedTaskId,
        Guid? ColumnId,
        string? ColumnName,
        bool ColumnIsDone,
        Guid? MilestoneId,
        string? MilestoneName,
        DateTime CreatedAt,
        DateTime UpdatedAt
    );
}
