using System;
using System.Collections.Generic;
using System.Text.Json;
using Teammy.Application.Activity.Dtos;
using Teammy.Application.Activity.Services;
using Teammy.Application.Kanban.Interfaces;
using Teammy.Application.ProjectTracking.Dtos;
using Teammy.Application.ProjectTracking.Interfaces;

namespace Teammy.Application.ProjectTracking.Services;

public sealed class ProjectTrackingService(
    IProjectTrackingReadOnlyQueries read,
    IProjectTrackingRepository repo,
    IKanbanRepository kanban,
    IGroupAccessQueries access,
    ActivityLogService activityLog
)
{
    public async Task<IReadOnlyList<BacklogItemVm>> ListBacklogAsync(Guid groupId, Guid currentUserId, CancellationToken ct)
    {
        await EnsureViewerAccessAsync(groupId, currentUserId, ct);
        return await read.ListBacklogAsync(groupId, ct);
    }

    public async Task<Guid> CreateBacklogItemAsync(Guid groupId, Guid currentUserId, CreateBacklogItemRequest req, CancellationToken ct)
    {
        await EnsureMemberAsync(groupId, currentUserId, ct);
        ValidateBacklogTitle(req.Title);

        var payload = req with
        {
            Title = req.Title.Trim(),
            Description = string.IsNullOrWhiteSpace(req.Description) ? null : req.Description.Trim(),
            Priority = string.IsNullOrWhiteSpace(req.Priority) ? null : req.Priority.Trim(),
            Category = string.IsNullOrWhiteSpace(req.Category) ? null : req.Category.Trim()
        };

        return await repo.CreateBacklogItemAsync(groupId, currentUserId, payload, ct);
    }

    public async Task UpdateBacklogItemAsync(Guid groupId, Guid backlogItemId, Guid currentUserId, UpdateBacklogItemRequest req, CancellationToken ct)
    {
        await EnsureMemberAsync(groupId, currentUserId, ct);
        ValidateBacklogTitle(req.Title);

        var payload = req with
        {
            Title = req.Title.Trim(),
            Description = string.IsNullOrWhiteSpace(req.Description) ? null : req.Description.Trim(),
            Priority = string.IsNullOrWhiteSpace(req.Priority) ? null : req.Priority.Trim(),
            Category = string.IsNullOrWhiteSpace(req.Category) ? null : req.Category.Trim(),
            Status = req.Status.Trim()
        };

        await repo.UpdateBacklogItemAsync(backlogItemId, groupId, payload, ct);
    }

    public async Task ArchiveBacklogItemAsync(Guid groupId, Guid backlogItemId, Guid currentUserId, CancellationToken ct)
    {
        await EnsureMemberAsync(groupId, currentUserId, ct);
        await repo.ArchiveBacklogItemAsync(backlogItemId, groupId, ct);
    }

    public async Task<Guid> PromoteBacklogItemAsync(Guid groupId, Guid backlogItemId, Guid currentUserId, PromoteBacklogItemRequest req, CancellationToken ct)
    {
        await EnsureMemberAsync(groupId, currentUserId, ct);
        ValidateColumnId(req.ColumnId);

        var item = await read.GetBacklogItemAsync(backlogItemId, groupId, ct)
                  ?? throw new KeyNotFoundException("Backlog item not found");
        if (item.GroupId != groupId)
            throw new UnauthorizedAccessException("Backlog item does not belong to this group");
        if (string.Equals(item.Status, "archived", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Cannot promote an archived backlog item");
        if (item.LinkedTaskId is not null)
            throw new InvalidOperationException("Backlog item is already on the board");

        var taskStatus = string.IsNullOrWhiteSpace(req.TaskStatus) ? null : req.TaskStatus.Trim();
        var taskDueDate = req.TaskDueDate ?? item.DueDate;

        await kanban.EnsureBoardForGroupAsync(groupId, ct);
        return await kanban.CreateTaskAsync(
            groupId,
            req.ColumnId,
            item.Title,
            item.Description,
            item.Priority,
            taskStatus,
            taskDueDate,
            backlogItemId,
            ct);
    }

    public async Task<IReadOnlyList<MilestoneVm>> ListMilestonesAsync(Guid groupId, Guid currentUserId, CancellationToken ct)
    {
        await EnsureViewerAccessAsync(groupId, currentUserId, ct);
        return await read.ListMilestonesAsync(groupId, ct);
    }

    public async Task<Guid> CreateMilestoneAsync(Guid groupId, Guid currentUserId, CreateMilestoneRequest req, CancellationToken ct)
    {
        await EnsureLeaderAsync(groupId, currentUserId, ct);
        ValidateMilestoneName(req.Name);

        var payload = req with { Name = req.Name.Trim(), Description = string.IsNullOrWhiteSpace(req.Description) ? null : req.Description.Trim() };
        return await repo.CreateMilestoneAsync(groupId, currentUserId, payload, ct);
    }

    public async Task UpdateMilestoneAsync(Guid groupId, Guid milestoneId, Guid currentUserId, UpdateMilestoneRequest req, CancellationToken ct)
    {
        await EnsureLeaderAsync(groupId, currentUserId, ct);
        ValidateMilestoneName(req.Name);

        var payload = req with { Name = req.Name.Trim(), Description = string.IsNullOrWhiteSpace(req.Description) ? null : req.Description.Trim(), Status = req.Status.Trim() };
        await repo.UpdateMilestoneAsync(milestoneId, groupId, payload, ct);
    }

    public async Task DeleteMilestoneAsync(Guid groupId, Guid milestoneId, Guid currentUserId, CancellationToken ct)
    {
        await EnsureLeaderAsync(groupId, currentUserId, ct);
        await repo.DeleteMilestoneAsync(milestoneId, groupId, ct);
    }

    public async Task AssignMilestoneItemsAsync(Guid groupId, Guid milestoneId, Guid currentUserId, AssignMilestoneItemsRequest req, CancellationToken ct)
    {
        await EnsureLeaderAsync(groupId, currentUserId, ct);
        if (req.BacklogItemIds is null || req.BacklogItemIds.Count == 0)
            throw new ArgumentException("BacklogItemIds is required", nameof(req.BacklogItemIds));

        await repo.AssignMilestoneItemsAsync(milestoneId, groupId, req.BacklogItemIds, ct);
    }

    public async Task RemoveMilestoneItemAsync(Guid groupId, Guid milestoneId, Guid backlogItemId, Guid currentUserId, CancellationToken ct)
    {
        await EnsureLeaderAsync(groupId, currentUserId, ct);
        await repo.RemoveMilestoneItemAsync(milestoneId, backlogItemId, groupId, ct);
    }

    public async Task<ProjectReportVm> GetProjectReportAsync(Guid groupId, Guid currentUserId, Guid? milestoneId, CancellationToken ct)
    {
        await EnsureViewerAccessAsync(groupId, currentUserId, ct);
        return await read.BuildProjectReportAsync(groupId, milestoneId, ct);
    }

    public async Task<MemberScoreReportVm> GetMemberScoresAsync(Guid groupId, Guid currentUserId, MemberScoreQuery req, CancellationToken ct)
    {
        await EnsureViewerAccessAsync(groupId, currentUserId, ct);
        if (req.From > req.To)
            throw new ArgumentException("From must be before To", nameof(req));

        static int ClampWeight(int? value, int fallback)
            => value.HasValue ? Math.Max(0, value.Value) : fallback;

        var weights = new MemberScoreWeightsVm(
            ClampWeight(req.High, 5),
            ClampWeight(req.Medium, 3),
            ClampWeight(req.Low, 1)
        );

        var fromUtc = DateTime.SpecifyKind(req.From.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);
        var toUtc = DateTime.SpecifyKind(req.To.ToDateTime(TimeOnly.MaxValue), DateTimeKind.Utc);

        return await read.BuildMemberScoresReportAsync(groupId, fromUtc, toUtc, weights, ct);
    }

    public async Task<MilestoneOverdueActionsVm?> GetMilestoneOverdueActionsAsync(Guid groupId, Guid milestoneId, Guid currentUserId, CancellationToken ct)
    {
        await EnsureViewerAccessAsync(groupId, currentUserId, ct);
        return await read.GetMilestoneOverdueActionsAsync(milestoneId, groupId, ct);
    }

    public async Task<MilestoneActionResultVm> ExtendMilestoneAsync(Guid groupId, Guid milestoneId, Guid currentUserId, ExtendMilestoneRequest req, CancellationToken ct)
    {
        await EnsureLeaderAsync(groupId, currentUserId, ct);

        var milestone = await read.GetMilestoneAsync(milestoneId, ct)
            ?? throw new KeyNotFoundException("Milestone not found");

        if (milestone.GroupId != groupId)
            throw new UnauthorizedAccessException("Milestone does not belong to this group");

        var oldTargetDate = milestone.TargetDate;
        await repo.ExtendMilestoneTargetDateAsync(milestoneId, groupId, req.NewTargetDate, ct);

        // Log activity
        await activityLog.LogAsync(new ActivityLogCreateRequest(currentUserId, "milestone", "extend_target_date")
        {
            GroupId = groupId,
            EntityId = milestoneId,
            Message = $"Extended milestone '{milestone.Name}' target date from {oldTargetDate} to {req.NewTargetDate}",
            Metadata = new Dictionary<string, object>
            {
                ["old_target_date"] = oldTargetDate?.ToString() ?? "",
                ["new_target_date"] = req.NewTargetDate.ToString(),
                ["milestone_name"] = milestone.Name
            }
        }, ct);

        return new MilestoneActionResultVm(
            Success: true,
            Action: "extend",
            Message: $"Milestone target date extended to {req.NewTargetDate}",
            NewMilestoneId: null
        );
    }

    public async Task<MilestoneActionResultVm> MoveMilestoneTasksAsync(Guid groupId, Guid milestoneId, Guid currentUserId, MoveMilestoneTasksRequest req, CancellationToken ct)
    {
        await EnsureLeaderAsync(groupId, currentUserId, ct);

        var sourceMilestone = await read.GetMilestoneAsync(milestoneId, ct)
            ?? throw new KeyNotFoundException("Source milestone not found");

        if (sourceMilestone.GroupId != groupId)
            throw new UnauthorizedAccessException("Milestone does not belong to this group");

        Guid? newMilestoneId = null;
        Guid targetMilestoneId;

        // Nếu cần tạo milestone mới
        if (req.CreateNewMilestone)
        {
            if (string.IsNullOrWhiteSpace(req.NewMilestoneName))
                throw new ArgumentException("NewMilestoneName is required when creating new milestone", nameof(req));

            // ID sẽ được PostgreSQL tự động generate
            newMilestoneId = await repo.CreateMilestoneAsync(
                groupId,
                currentUserId,
                new CreateMilestoneRequest(
                    req.NewMilestoneName.Trim(),
                    req.NewMilestoneDescription?.Trim(),
                    req.NewMilestoneTargetDate
                ),
                ct);

            targetMilestoneId = newMilestoneId.Value;
        }
        else
        {
            // Khi không tạo mới, cần TargetMilestoneId
            if (!req.TargetMilestoneId.HasValue || req.TargetMilestoneId.Value == Guid.Empty)
                throw new ArgumentException("TargetMilestoneId is required when not creating new milestone", nameof(req));

            targetMilestoneId = req.TargetMilestoneId.Value;
            var targetMilestoneCheck = await read.GetMilestoneAsync(targetMilestoneId, ct)
                ?? throw new KeyNotFoundException("Target milestone not found");
            if (targetMilestoneCheck.GroupId != groupId)
                throw new UnauthorizedAccessException("Target milestone does not belong to this group");
        }

        var movedMilestoneId = await repo.MoveIncompleteTasksToMilestoneAsync(
            milestoneId,
            groupId,
            targetMilestoneId,
            newMilestoneId,
            ct);

        var targetMilestoneInfo = await read.GetMilestoneAsync(targetMilestoneId, ct);
        var targetMilestoneName = req.CreateNewMilestone
            ? req.NewMilestoneName
            : targetMilestoneInfo?.Name ?? "Unknown";

        // Log activity
        await activityLog.LogAsync(new ActivityLogCreateRequest(currentUserId, "milestone", "move_incomplete_tasks")
        {
            GroupId = groupId,
            EntityId = milestoneId,
            Message = $"Moved incomplete tasks from milestone '{sourceMilestone.Name}' to '{targetMilestoneName}'",
            Metadata = new Dictionary<string, object>
            {
                ["source_milestone_id"] = milestoneId.ToString(),
                ["source_milestone_name"] = sourceMilestone.Name,
                ["target_milestone_id"] = movedMilestoneId.ToString(),
                ["target_milestone_name"] = targetMilestoneName,
                ["created_new_milestone"] = req.CreateNewMilestone
            }
        }, ct);

        return new MilestoneActionResultVm(
            Success: true,
            Action: "move_tasks",
            Message: $"Incomplete tasks moved to milestone '{targetMilestoneName}'",
            NewMilestoneId: newMilestoneId
        );
    }

    public async Task<TimelineVm> GetTimelineAsync(Guid groupId, Guid currentUserId, DateOnly? startDate, DateOnly? endDate, CancellationToken ct)
    {
        await EnsureViewerAccessAsync(groupId, currentUserId, ct);
        return await read.GetTimelineAsync(groupId, startDate, endDate, ct);
    }

    private async Task EnsureViewerAccessAsync(Guid groupId, Guid userId, CancellationToken ct)
    {
        if (await access.IsMemberAsync(groupId, userId, ct)) return;
        if (await access.IsMentorAsync(groupId, userId, ct)) return;
        throw new UnauthorizedAccessException("Not allowed to view tracking data");
    }

    private async Task EnsureMemberAsync(Guid groupId, Guid userId, CancellationToken ct)
    {
        if (!await access.IsMemberAsync(groupId, userId, ct))
            throw new UnauthorizedAccessException("Not a group member");
    }

    private async Task EnsureLeaderAsync(Guid groupId, Guid userId, CancellationToken ct)
    {
        if (!await access.IsLeaderAsync(groupId, userId, ct))
            throw new UnauthorizedAccessException("Only group leader can manage milestones");
    }

    private static void ValidateBacklogTitle(string title)
    {
        if (string.IsNullOrWhiteSpace(title))
            throw new ArgumentException("Title is required", nameof(title));
    }

    private static void ValidateMilestoneName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Name is required", nameof(name));
    }

    private static void ValidateColumnId(Guid columnId)
    {
        if (columnId == Guid.Empty)
            throw new ArgumentException("ColumnId is required", nameof(columnId));
    }
}
