using System;
using System.Collections.Generic;
using Teammy.Application.Kanban.Interfaces;
using Teammy.Application.ProjectTracking.Dtos;
using Teammy.Application.ProjectTracking.Interfaces;

namespace Teammy.Application.ProjectTracking.Services;

public sealed class ProjectTrackingService(
    IProjectTrackingReadOnlyQueries read,
    IProjectTrackingRepository repo,
    IKanbanRepository kanban,
    IGroupAccessQueries access
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
